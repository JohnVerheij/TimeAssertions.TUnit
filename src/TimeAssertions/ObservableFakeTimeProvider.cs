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
    public void Advance(TimeSpan delta) => Fake.Advance(delta);

    /// <summary>Sets the fake clock to <paramref name="value"/>, firing every timer that becomes due.</summary>
    /// <param name="value">The new date and time.</param>
    public void SetUtcNow(DateTimeOffset value) => Fake.SetUtcNow(value);

    /// <summary>
    /// Advances the fake clock by <paramref name="delta"/> and then waits until the loops woken by it have
    /// re-armed: for every one-shot timer that fired during the advance, a timer has been armed, either created or
    /// re-scheduled with <see cref="ITimer.Change"/> (a callback that re-arms its own one-shot timer).
    /// </summary>
    /// <param name="delta">The amount of time to advance.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>A task that completes once the loops woken by the advance have created their next timers.</returns>
    /// <remarks>
    /// <para>
    /// A one-shot timer that fired is done; a loop that keeps running schedules its next wait by creating a
    /// new timer or re-scheduling one, so "as many armed timers as fired one-shot timers" means every woken loop
    /// reached its next wait. Periodic timers keep their schedule and are not waited for; use
    /// <see cref="AdvanceUntilAsync"/> for loops driven by a periodic timer.
    /// </para>
    /// <para>
    /// The counts are those of the whole provider: a timer that other code creates or re-schedules during the
    /// wait counts as well, so the settle can return before a woken loop has finished its step. Rely on it when
    /// the loops being advanced are the only code arming timers on this provider; otherwise wait for the loop's
    /// own signal, for example a <see cref="ObservableTimeProvider.WaitUntilAsync"/> condition on its state.
    /// </para>
    /// <para>
    /// A loop that ends after its timer fired arms no new one, so the wait then runs until
    /// <paramref name="cancellationToken"/> is canceled. The resulting <see cref="OperationCanceledException"/>
    /// says how many timers fired and how many were armed.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before
    /// the woken loops re-armed.</exception>
    public async Task AdvanceAndSettleAsync(TimeSpan delta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var armedBefore = TimersArmedCount;
        var oneShotFiresBefore = OneShotFireCount;
        Fake.Advance(delta);
        var fired = OneShotFireCount - oneShotFiresBefore;

        try
        {
            await WaitUntilAsync(provider => provider.TimersArmedCount - armedBefore >= fired, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Advancing {delta} fired {fired} one-shot timer(s), but only {TimersArmedCount - armedBefore} timer(s) were armed (created, or re-scheduled with Change) before the wait was canceled. A loop that ends after its timer fires arms no new one; for a periodic timer use AdvanceUntilAsync."),
                exception,
                cancellationToken);
        }
    }

    /// <summary>
    /// Advances the fake clock by <paramref name="step"/> each time the code under test has created a timer,
    /// until <paramref name="until"/> completes. Drives a loop through any number of timed waits (for example
    /// a reconnect backoff per failed attempt) without real time: it advances only once a timer exists, so no
    /// advance is lost.
    /// </summary>
    /// <param name="until">The task whose completion ends the advancing, for example a start that returns once connected.</param>
    /// <param name="step">The fake time per advance; at least the longest single wait the loop schedules.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>A task that completes once <paramref name="until"/> has completed. It does not observe the
    /// outcome of <paramref name="until"/>; await that task afterwards.</returns>
    /// <remarks>
    /// The first advance happens as soon as any timer has been created through this provider, including one
    /// created before the call; after that, each advance waits for a timer created since the previous one.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="until"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is not positive.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before
    /// <paramref name="until"/> completed.</exception>
    [SuppressMessage(
        "Usage",
        "VSTHRD003:Avoid awaiting foreign Tasks",
        Justification = "Observing the caller's task is the documented contract of this method: it advances until that task completes. Test code runs without a SynchronizationContext or JoinableTaskFactory, so there is no thread to deadlock.")]
    public async Task AdvanceOnEachTimerUntilAsync(Task until, TimeSpan step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(until);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);

        long handled = 0;
        while (!until.IsCompleted)
        {
            var nextTimer = WaitForTimersCreatedAsync(handled + 1, cancellationToken);
            if (await Task.WhenAny(nextTimer, until).ConfigureAwait(false) == until)
            {
                return;
            }

            // Surfaces a cancellation of the wait instead of advancing.
            await nextTimer.ConfigureAwait(false);
            handled = TimersCreatedCount;
            Fake.Advance(step);
        }
    }

    /// <summary>
    /// Advances the fake clock in steps of <paramref name="step"/>, settling after each step (see
    /// <see cref="AdvanceAndSettleAsync"/>), until <paramref name="until"/> holds or
    /// <paramref name="maxAdvance"/> of fake time has passed. Suits loops driven by a periodic timer, for
    /// example "until <see cref="ObservableTimeProvider.TimerFireCount"/> reaches 6".
    /// </summary>
    /// <param name="step">The fake time per advance.</param>
    /// <param name="until">The condition to reach; checked before each step, on the calling thread.</param>
    /// <param name="maxAdvance">The most fake time to advance before giving up; the last step is shortened so
    /// the clock never moves past it. A fake-time bound, so the outcome is deterministic.</param>
    /// <param name="cancellationToken">Bounds the settle waits, typically the test's token.</param>
    /// <returns>A task that completes once <paramref name="until"/> holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="until"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is not positive, or
    /// <paramref name="maxAdvance"/> is negative.</exception>
    /// <exception cref="TimeoutException"><paramref name="until"/> did not hold after advancing
    /// <paramref name="maxAdvance"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled during a
    /// settle wait.</exception>
    public async Task AdvanceUntilAsync(TimeSpan step, Func<bool> until, TimeSpan maxAdvance, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(until);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAdvance, TimeSpan.Zero);

        var advanced = TimeSpan.Zero;
        while (!until())
        {
            if (advanced >= maxAdvance)
            {
                throw new TimeoutException(
                    $"The condition did not hold after advancing {advanced} of fake time in steps of {step}.");
            }

            var next = step < maxAdvance - advanced ? step : maxAdvance - advanced;
            await AdvanceAndSettleAsync(next, cancellationToken).ConfigureAwait(false);
            advanced += next;
        }
    }
}
