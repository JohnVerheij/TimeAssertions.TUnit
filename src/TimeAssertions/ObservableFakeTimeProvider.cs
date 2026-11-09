using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace TimeAssertions;

/// <summary>
/// An <see cref="ObservableTimeProvider"/> over its own <see cref="FakeTimeProvider"/>: one object to inject
/// into the code under test, to advance, and to assert on. Every <see cref="ObservableTimeProvider"/> member
/// and assertion applies unchanged, and the advance helpers drive timer-based loops without real time:
/// <see cref="AdvanceAndSettleAsync"/>, <see cref="AdvanceOnEachTimerUntilAsync"/>, and
/// <see cref="AdvanceUntilAsync"/>.
/// </summary>
/// <remarks>
/// Code under test that waits on the injected provider (a <c>Task.Delay(delay, timeProvider, ct)</c>, a
/// <c>new CancellationTokenSource(timeout, timeProvider)</c>, a <c>WaitAsync(timeout, timeProvider, ct)</c>)
/// creates one timer per wait, often on a background thread. The helpers advance only once such a timer
/// exists, so an advance can never come too early, and they never sleep in real time.
/// </remarks>
public sealed class ObservableFakeTimeProvider : ObservableTimeProvider
{
    /// <summary>Initializes a provider over a new <see cref="FakeTimeProvider"/> with its default start time.</summary>
    public ObservableFakeTimeProvider()
        : this(new FakeTimeProvider())
    {
    }

    /// <summary>Initializes a provider over a new <see cref="FakeTimeProvider"/> starting at <paramref name="startDateTime"/>.</summary>
    /// <param name="startDateTime">The initial date and time of the fake clock.</param>
    public ObservableFakeTimeProvider(DateTimeOffset startDateTime)
        : this(new FakeTimeProvider(startDateTime))
    {
    }

    private ObservableFakeTimeProvider(FakeTimeProvider fake)
        : base(fake)
    {
        Fake = fake;
    }

    /// <summary>
    /// Gets the owned <see cref="FakeTimeProvider"/>, for its other settings (for example
    /// <see cref="FakeTimeProvider.AutoAdvanceAmount"/> or <see cref="FakeTimeProvider.SetLocalTimeZone"/>).
    /// Inject this <see cref="ObservableFakeTimeProvider"/> into the code under test, not the fake itself:
    /// timers created directly on the fake are not observed.
    /// </summary>
    public FakeTimeProvider Fake { get; }

    /// <summary>Advances the fake clock by <paramref name="delta"/>, firing every timer that becomes due.</summary>
    /// <param name="delta">The amount of time to advance.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative, or would move the clock
    /// past <see cref="DateTimeOffset.MaxValue"/>. While <see cref="FakeTimeProvider.AutoAdvanceAmount"/> is set, that
    /// check is left to the fake clock, because reading the clock would then move it.</exception>
    public void Advance(TimeSpan delta)
    {
        ThrowIfNegative(delta, nameof(delta));
        AdvanceClock(delta, nameof(delta));
    }

    /// <summary>Sets the fake clock to <paramref name="value"/>, firing every timer that becomes due.</summary>
    /// <param name="value">The new date and time.</param>
    public void SetUtcNow(DateTimeOffset value) => Fake.SetUtcNow(value);

    /// <summary>
    /// Advances the fake clock by <paramref name="delta"/> and then waits until the loops woken by it have
    /// re-armed: for every one-shot timer that fired during the advance, a timer has been armed, either created or
    /// re-armed with <see cref="ITimer.Change"/> (a callback that re-arms its own one-shot timer).
    /// </summary>
    /// <param name="delta">The amount of time to advance.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token. To fail sooner than
    /// the test's timeout, pass a linked token with <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>.</param>
    /// <returns>A task that completes once the loops woken by the advance have armed their next timers.</returns>
    /// <remarks>
    /// <para>
    /// A one-shot timer that fired is done; a loop that keeps running schedules its next wait by creating a
    /// timer with a due time or by re-arming one with <see cref="ITimer.Change"/>: a fired one-shot timer, or one
    /// created disabled. So "as many armed timers as fired one-shot timers" means every woken loop reached its next
    /// wait. Moving a timer that is still pending is not an arm, so a loop that extends a watchdog deadline
    /// (<c>CancelAfter</c>) before its next wait is waited for until that wait exists. Periodic timers keep their
    /// schedule and are not waited for; use <see cref="AdvanceUntilAsync"/> for loops driven by a periodic timer.
    /// </para>
    /// <para>
    /// The counts are those of the whole provider: a timer that other code creates or re-arms during the
    /// wait counts as well, so the settle can return before a woken loop has finished its step. Rely on it when
    /// the loops being advanced are the only code arming timers on this provider; otherwise wait for the loop's
    /// own signal, for example a <see cref="ObservableTimeProvider.WaitUntilAsync"/> condition on its state.
    /// </para>
    /// <para>
    /// A loop that ends after its timer fired arms no new one, so the wait then runs until
    /// <paramref name="cancellationToken"/> is canceled. The resulting <see cref="OperationCanceledException"/>
    /// says how many timers fired and how many were armed. A token that is already canceled stops the call before
    /// the clock moves.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative, or would move the clock
    /// past <see cref="DateTimeOffset.MaxValue"/>. While <see cref="FakeTimeProvider.AutoAdvanceAmount"/> is set, that
    /// check is left to the fake clock, because reading the clock would then move it.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before
    /// the advance, or before the woken loops re-armed.</exception>
    public Task AdvanceAndSettleAsync(TimeSpan delta, CancellationToken cancellationToken)
        => AdvanceAndSettleCoreAsync(
            delta,
            nameof(delta),
            new AdvanceProgress(0, delta, TimeSpan.Zero, Shortened: false),
            " A loop that ends after its timer fires arms no new one; for a periodic timer use AdvanceUntilAsync.",
            cancellationToken);

    /// <summary>
    /// Advances the fake clock by <paramref name="step"/> each time a timer has been created through this provider
    /// since the previous advance (for the first advance, any timer, including one created before the call), until
    /// <paramref name="until"/> completes; then completes as <paramref name="until"/> did. Drives a loop through any
    /// number of timed waits (for example a reconnect backoff per failed attempt) without real time: it advances
    /// only once a timer exists, so no advance is lost.
    /// </summary>
    /// <param name="until">The task whose completion ends the advancing, for example a start that returns once connected.</param>
    /// <param name="step">The fake time per advance; at least the longest single wait the loop schedules.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token. To fail sooner than
    /// the test's timeout, pass a linked token with <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>.</param>
    /// <returns>A task that completes once <paramref name="until"/> has completed successfully.</returns>
    /// <remarks>
    /// <para>
    /// The outcome of <paramref name="until"/> is observed as <c>await until</c> observes it: a fault rethrows the
    /// task's (first) exception and a cancellation rethrows its <see cref="OperationCanceledException"/>, so a failed
    /// task cannot go unnoticed. When the test expects the fault, assert that this call throws it. When
    /// <paramref name="until"/> has completed by the time <paramref name="cancellationToken"/> is canceled, its
    /// outcome is the one reported. For a task with a result, use
    /// <see cref="AdvanceOnEachTimerUntilAsync{T}(Task{T}, TimeSpan, CancellationToken)"/>, which returns it.
    /// </para>
    /// <para>
    /// The counts are those of the whole provider, so use it on a provider the loop has to itself. A loop in a wait
    /// longer than <paramref name="step"/> is not woken by an advance and creates no new timer, so the call then runs
    /// until <paramref name="cancellationToken"/> is canceled; the <see cref="OperationCanceledException"/> says how
    /// many advances were made, how far the clock moved, and how many timers were created.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="until"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is not positive, or an advance by it
    /// would move the clock past <see cref="DateTimeOffset.MaxValue"/>. While
    /// <see cref="FakeTimeProvider.AutoAdvanceAmount"/> is set, that check is left to the fake clock, because reading
    /// the clock would then move it.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before
    /// <paramref name="until"/> completed, or <paramref name="until"/> was canceled.</exception>
    [SuppressMessage(
        "Usage",
        "VSTHRD003:Avoid awaiting foreign Tasks",
        Justification = "Observing the caller's task is the documented contract of this method: it advances until that task completes. Test code runs without a SynchronizationContext or JoinableTaskFactory, so there is no thread to deadlock.")]
    public async Task AdvanceOnEachTimerUntilAsync(Task until, TimeSpan step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(until);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);

        long handled = 0;
        var progress = new AdvanceProgress(0, step, TimeSpan.Zero, Shortened: false);
        while (!until.IsCompleted)
        {
            var created = handled;
            using var timerWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var nextTimer = WaitUntilAsync(provider => provider.TimersCreatedCount > created, timerWait.Token);
            _ = await Task.WhenAny(nextTimer, until).ConfigureAwait(false);

            // The task's outcome wins over a cancellation that ended the wait at the same time. The timer wait
            // is ended and awaited here, so none outlives the call.
            if (until.IsCompleted)
            {
                await timerWait.CancelAsync().ConfigureAwait(false);
                await nextTimer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                break;
            }

            try
            {
                await nextTimer.ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                throw CanceledWhileAdvancing(progress, created + 1, exception, cancellationToken);
            }

            handled = TimersCreatedCount;
            AdvanceClock(step, nameof(step));
            progress = progress.After(step);
        }

        await until.ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the fake clock by <paramref name="step"/> each time a timer has been created through this provider
    /// since the previous advance, until <paramref name="until"/> completes, and returns its result. Behaves as
    /// <see cref="AdvanceOnEachTimerUntilAsync(Task, TimeSpan, CancellationToken)"/> in every other respect, for
    /// example for a task of LogAssertions' <c>WaitForLogAsync</c> on a <c>FakeLogCollector</c>, whose result is the
    /// awaited record.
    /// </summary>
    /// <typeparam name="T">The result type of <paramref name="until"/>.</typeparam>
    /// <param name="until">The task whose completion ends the advancing.</param>
    /// <param name="step">The fake time per advance; at least the longest single wait the loop schedules.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>The result of <paramref name="until"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="until"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is not positive, or an advance by it
    /// would move the clock past <see cref="DateTimeOffset.MaxValue"/>. While
    /// <see cref="FakeTimeProvider.AutoAdvanceAmount"/> is set, that check is left to the fake clock, because reading
    /// the clock would then move it.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before
    /// <paramref name="until"/> completed, or <paramref name="until"/> was canceled.</exception>
    [SuppressMessage(
        "Usage",
        "VSTHRD003:Avoid awaiting foreign Tasks",
        Justification = "Observing the caller's task is the documented contract of this method: it advances until that task completes. Test code runs without a SynchronizationContext or JoinableTaskFactory, so there is no thread to deadlock.")]
    public async Task<T> AdvanceOnEachTimerUntilAsync<T>(Task<T> until, TimeSpan step, CancellationToken cancellationToken)
    {
        Task completion = until;
        await AdvanceOnEachTimerUntilAsync(completion, step, cancellationToken).ConfigureAwait(false);
        return await until.ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the fake clock in steps of <paramref name="step"/>, settling after each step (see
    /// <see cref="AdvanceAndSettleAsync"/>), until <paramref name="until"/> holds or
    /// <paramref name="maxAdvance"/> of fake time has passed. Suits loops driven by a periodic timer, for
    /// example "until <see cref="ObservableTimeProvider.TimerFireCount"/> reaches 6".
    /// </summary>
    /// <remarks>
    /// A periodic timer gives no sign that its loop is waiting again, so the steps do not wait for the loop: a loop
    /// that awaits other work after each tick falls behind, and a <see cref="PeriodicTimer"/> that is not being
    /// awaited merges the ticks it missed. Base <paramref name="until"/> on what the ticks themselves do, such as
    /// <see cref="ObservableTimeProvider.TimerFireCount"/>, or wait for the loop's own signal after each
    /// <see cref="Advance"/>.
    /// </remarks>
    /// <param name="step">The fake time per advance.</param>
    /// <param name="until">The condition to reach; checked before each step, on the calling thread.</param>
    /// <param name="maxAdvance">The most fake time to advance before giving up; the last step is shortened so
    /// the clock never moves past it. A fake-time bound, so the outcome is deterministic.</param>
    /// <param name="cancellationToken">Bounds the settle waits, typically the test's token.</param>
    /// <returns>A task that completes once <paramref name="until"/> holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="until"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is not positive, a step would move the
    /// clock past <see cref="DateTimeOffset.MaxValue"/>, or <paramref name="maxAdvance"/> is negative. While
    /// <see cref="FakeTimeProvider.AutoAdvanceAmount"/> is set, the check against the latest time is left to the fake
    /// clock, because reading the clock would then move it.</exception>
    /// <exception cref="TimeoutException"><paramref name="until"/> did not hold after advancing
    /// <paramref name="maxAdvance"/>. The message says how many advances were made and how far the clock moved.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before an
    /// advance or during a settle wait. The message says how many advances were made and how far the clock moved,
    /// and for a settle wait how many timers the last advance fired and armed.</exception>
    public async Task AdvanceUntilAsync(TimeSpan step, Func<bool> until, TimeSpan maxAdvance, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(until);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAdvance, TimeSpan.Zero);

        var progress = new AdvanceProgress(0, step, TimeSpan.Zero, Shortened: false);
        while (!until())
        {
            if (progress.Total >= maxAdvance)
            {
                throw new TimeoutException(
                    $"{Describe(progress)}, but the condition did not hold within the maximum advance of {maxAdvance}.");
            }

            var next = step < maxAdvance - progress.Total ? step : maxAdvance - progress.Total;
            await AdvanceAndSettleCoreAsync(
                next,
                nameof(step),
                progress,
                " A loop that ends after its timer fires arms no new one.",
                cancellationToken).ConfigureAwait(false);
            progress = progress.After(next);
        }
    }

    /// <summary>
    /// Advances by <paramref name="delta"/> and waits until as many timers have been armed as one-shot timers fired.
    /// On cancellation, throws an <see cref="OperationCanceledException"/> that describes the calling helper's
    /// advances and the counts; a token that is already canceled stops it before the clock moves.
    /// </summary>
    /// <param name="delta">The amount of time to advance.</param>
    /// <param name="deltaName">The calling helper's name for <paramref name="delta"/>, for argument exceptions.</param>
    /// <param name="done">The calling helper's advances before this one, for the message.</param>
    /// <param name="hint">The sentence that ends the message of a canceled settle wait, with a leading space.</param>
    /// <param name="cancellationToken">The only bound of the wait.</param>
    /// <returns>A task that completes once the woken loops have armed their next timers.</returns>
    private async Task AdvanceAndSettleCoreAsync(
        TimeSpan delta,
        string deltaName,
        AdvanceProgress done,
        string hint,
        CancellationToken cancellationToken)
    {
        ThrowIfNegative(delta, deltaName);
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"{Describe(done)}, but the token was canceled before the {(done.Count is 0 ? "first" : "next")} advance.",
                cancellationToken);
        }

        var progress = done.After(delta);
        var armedBefore = TimersArmedCount;
        var oneShotFiresBefore = OneShotFireCount;
        AdvanceClock(delta, deltaName);
        var fired = OneShotFireCount - oneShotFiresBefore;

        try
        {
            await WaitUntilAsync(provider => provider.TimersArmedCount - armedBefore >= fired, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            var armed = TimersArmedCount - armedBefore;
            var message = armed >= fired
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(progress)}: the last advance fired {fired} one-shot timer(s), and {armed} timer(s) were armed, but the token was canceled before the settle returned.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(progress)}: the last advance fired {fired} one-shot timer(s), but only {armed} timer(s) were armed (created with a due time, or re-armed with Change; moving a pending timer is not an arm) before the wait was canceled.{hint}");
            throw new OperationCanceledException(message, exception, cancellationToken);
        }
    }

    /// <summary>Rejects a negative advance, naming the calling helper's argument.</summary>
    /// <param name="delta">The amount of time to advance.</param>
    /// <param name="deltaName">The calling helper's name for <paramref name="delta"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    private static void ThrowIfNegative(TimeSpan delta, string deltaName)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(deltaName, delta, "The fake clock cannot be advanced by a negative amount.");
        }
    }

    /// <summary>Advances the fake clock by <paramref name="delta"/>, rejecting an advance past
    /// <see cref="DateTimeOffset.MaxValue"/> before the clock moves.</summary>
    /// <param name="delta">The amount of time to advance; not negative.</param>
    /// <param name="deltaName">The calling helper's name for <paramref name="delta"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The advance would move the clock past
    /// <see cref="DateTimeOffset.MaxValue"/>.</exception>
    private void AdvanceClock(TimeSpan delta, string deltaName)
    {
        // With AutoAdvanceAmount set, reading the clock moves it, so the check is left to the fake then.
        if (Fake.AutoAdvanceAmount == TimeSpan.Zero && delta > DateTimeOffset.MaxValue - Fake.GetUtcNow())
        {
            throw new ArgumentOutOfRangeException(
                deltaName,
                delta,
                "The advance would move the fake clock past DateTimeOffset.MaxValue.");
        }

        Fake.Advance(delta);
    }

    /// <summary>Builds the exception for a cancellation that ended <c>AdvanceOnEachTimerUntilAsync</c> before its
    /// task completed, with the advances made and the timers created.</summary>
    /// <param name="progress">The advances made.</param>
    /// <param name="awaitedTimer">The cumulative number of the timer the next advance waited for.</param>
    /// <param name="inner">The cancellation of the wait for that timer.</param>
    /// <param name="cancellationToken">The token that was canceled.</param>
    /// <returns>The exception to throw.</returns>
    private OperationCanceledException CanceledWhileAdvancing(
        AdvanceProgress progress,
        long awaitedTimer,
        OperationCanceledException inner,
        CancellationToken cancellationToken)
    {
        var message = progress.Count is 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{Describe(progress)}, but the task had not completed when the wait was canceled: the first advance waited for timer {awaitedTimer}, and {TimersCreatedCount} timer(s) were created.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{Describe(progress)}, but the task had not completed when the wait was canceled: the next advance waited for timer {awaitedTimer}, and {TimersCreatedCount} timer(s) were created. A loop in a wait longer than the step is not woken by an advance, so it creates no new timer.");
        return new OperationCanceledException(message, inner, cancellationToken);
    }

    /// <summary>Describes the advances an advance helper made, the same way in every helper's message:
    /// "Advanced 3 time(s) by 00:00:01, 00:00:03 in total (clock now ...)".</summary>
    /// <param name="progress">The advances made.</param>
    /// <returns>The description, with the clock's time in the invariant round-trip format.</returns>
    private string Describe(AdvanceProgress progress) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Advanced {progress.Count} time(s) by {(progress.Shortened ? "up to " : string.Empty)}{progress.Step}, {progress.Total} in total (clock now {GetUtcNow():O})");

    /// <summary>The advances an advance helper has made, for its failure messages.</summary>
    /// <param name="Count">The number of advances made.</param>
    /// <param name="Step">The fake time per advance.</param>
    /// <param name="Total">The fake time advanced in total.</param>
    /// <param name="Shortened">Whether the last advance was shorter than <paramref name="Step"/>.</param>
    private readonly record struct AdvanceProgress(long Count, TimeSpan Step, TimeSpan Total, bool Shortened)
    {
        /// <summary>The progress after one more advance by <paramref name="delta"/>.</summary>
        /// <param name="delta">The fake time of the advance.</param>
        /// <returns>The new progress.</returns>
        public AdvanceProgress After(TimeSpan delta) => new(Count + 1, Step, Total + delta, delta < Step);
    }
}
