using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>
/// Tests for <see cref="ObservableFakeTimeProvider"/>: one object that is injected, advanced, and observed. The
/// advance helpers are driven against the loop shapes production code uses with an injected provider
/// (<c>Task.Delay(delay, timeProvider, ct)</c>, <c>new CancellationTokenSource(timeout, timeProvider)</c>,
/// <c>WaitAsync(timeout, timeProvider, ct)</c>, a reconnect backoff, a periodic timer), with no real time.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class ObservableFakeTimeProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FiveSeconds = TimeSpan.FromSeconds(5);

    // --- construction and the owned fake ---

    /// <summary>The parameterless constructor owns a fake at the fake's own default start time.</summary>
    [Test]
    public async Task Constructor_Default_UsesTheFakeDefaultStart(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider();

        await Assert.That(time.GetUtcNow()).IsEqualTo(new FakeTimeProvider().GetUtcNow());
        await Assert.That(time.GetUtcNow()).IsEqualTo(time.Fake.GetUtcNow());
    }

    /// <summary>The start-time constructor sets the fake clock.</summary>
    [Test]
    public async Task Constructor_StartDateTime_SetsTheClock(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);

        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary><see cref="ObservableFakeTimeProvider.Advance"/> moves the clock and fires the timers that
    /// became due; <see cref="ObservableFakeTimeProvider.SetUtcNow"/> does the same to an absolute time.</summary>
    [Test]
    public async Task AdvanceAndSetUtcNow_MoveTheClockAndFireDueTimers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, FiveSeconds, Timeout.InfiniteTimeSpan);

        time.Advance(OneSecond);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + OneSecond);
        await Assert.That(time.TimerFireCount).IsEqualTo(1);

        time.SetUtcNow(Start + FiveSeconds);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + FiveSeconds);
        await Assert.That(time.TimerFireCount).IsEqualTo(2);
    }

    /// <summary>A timer due at once fires during its creation; the provider counts both the creation and the
    /// fire, and reports the one-shot timer as disabled.</summary>
    [Test]
    public async Task CreateTimer_DueAtOnce_FiresAndIsObserved(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        var fired = 0;

        _ = time.CreateTimer(_ => fired++, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        await Assert.That(fired).IsEqualTo(1);
        await Assert.That(time.TimersCreatedCount).IsEqualTo(1);
        await Assert.That(time.TimerFireCount).IsEqualTo(1);
        await Assert.That(time.NextTimerDueTime).IsNull();
    }

    /// <summary>Timers created on <see cref="ObservableFakeTimeProvider.Fake"/> directly bypass the observation,
    /// as its documentation states: inject the observable provider, not the fake.</summary>
    [Test]
    public async Task Fake_TimersCreatedOnItDirectly_AreNotObserved(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);

        using var direct = time.Fake.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
        await Assert.That(time.TimersCreatedCount).IsEqualTo(0);
    }

    // --- AdvanceAndSettleAsync ---

    /// <summary>A <c>Task.Delay</c> loop: each settled advance runs exactly one more iteration, and the loop has
    /// armed its next delay when the settle returns.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_DelayLoop_RunsOneIterationPerAdvance(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var iterations = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = DelayLoopAsync(time, OneSecond, iterations, stop.Token);

        for (var expected = 1; expected <= 3; expected++)
        {
            await time.AdvanceAndSettleAsync(OneSecond, cancellationToken);
            await Assert.That(iterations.Value).IsEqualTo(expected);
            await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        }

        await stop.CancelAsync();
        await loop;
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
    }

    /// <summary>A loop bounded by <c>new CancellationTokenSource(timeout, timeProvider)</c>: each settled advance
    /// past the timeout counts one timed-out attempt, and the next attempt's timer exists afterwards.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_CancellationTokenSourceTimeoutLoop_CountsEachTimeout(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var timeouts = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = TimeoutSourceLoopAsync(time, FiveSeconds, timeouts, stop.Token);

        await time.AdvanceAndSettleAsync(FiveSeconds, cancellationToken);
        await time.AdvanceAndSettleAsync(FiveSeconds, cancellationToken);

        await Assert.That(timeouts.Value).IsEqualTo(2);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        await stop.CancelAsync();
        await loop;
    }

    /// <summary>A loop bounded by <c>WaitAsync(timeout, timeProvider, ct)</c>: each settled advance past the
    /// timeout counts one <see cref="TimeoutException"/>.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_WaitAsyncTimeoutLoop_CountsEachTimeout(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var timeouts = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = WaitAsyncTimeoutLoopAsync(time, FiveSeconds, timeouts, stop.Token);

        await time.AdvanceAndSettleAsync(FiveSeconds, cancellationToken);
        await time.AdvanceAndSettleAsync(FiveSeconds, cancellationToken);
        await time.AdvanceAndSettleAsync(FiveSeconds, cancellationToken);

        await Assert.That(timeouts.Value).IsEqualTo(3);
        await stop.CancelAsync();
        await loop;
    }

    /// <summary>A periodic timer keeps its schedule, so the settle does not wait for a new timer.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_PeriodicTimer_ReturnsWithoutWaiting(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var timer = time.CreateTimer(static _ => { }, state: null, OneSecond, OneSecond);

        await time.AdvanceAndSettleAsync(TimeSpan.FromSeconds(3), cancellationToken);

        await Assert.That(time.TimerFireCount).IsEqualTo(3);
        await Assert.That(time.TimersCreatedCount).IsEqualTo(1);
    }

    /// <summary>An advance that fires nothing settles at once.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_NothingDue_ReturnsAtOnce(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, FiveSeconds, Timeout.InfiniteTimeSpan);

        await time.AdvanceAndSettleAsync(OneSecond, cancellationToken);

        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + OneSecond);
        await Assert.That(time.TimerFireCount).IsEqualTo(0);
    }

    /// <summary>A loop that ends after its timer fired never re-arms, so the settle runs until its token is
    /// canceled and then says how many timers fired and how many were created.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_LoopEnds_CanceledWithDiagnostics(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var settle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var finalRun = Task.Delay(OneSecond, time, cancellationToken);

        // Cancel the settle once the timer has fired, which happens inside the settle's own advance.
        _ = Task.Run(
            async () =>
            {
                await time.WaitUntilAsync(static provider => provider.TimerFireCount >= 1, cancellationToken);
                await settle.CancelAsync();
            },
            cancellationToken);

        var exception = await Assert.That(async () => await time.AdvanceAndSettleAsync(OneSecond, settle.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.Message).Contains("fired 1 one-shot timer(s), but only 0 timer(s) were armed");
        await Assert.That(exception.CancellationToken).IsEqualTo(settle.Token);
        await finalRun;
    }

    /// <summary>A callback that re-arms its own one-shot timer with <c>Change</c> (the no-overlap
    /// <c>System.Threading.Timer</c> pattern) counts as arming its next wait, so each settle returns.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_CallbackReArmsItsTimerWithChange_Settles(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var fires = new Counter();
        ITimer? timer = null;
        timer = time.CreateTimer(
            _ =>
            {
                fires.Increment();
                timer!.Change(OneSecond, Timeout.InfiniteTimeSpan);
            },
            state: null,
            OneSecond,
            Timeout.InfiniteTimeSpan);

        await time.AdvanceAndSettleAsync(OneSecond, cancellationToken);
        await time.AdvanceAndSettleAsync(OneSecond, cancellationToken);

        await Assert.That(fires.Value).IsEqualTo(2);
        await Assert.That(time.TimersCreatedCount).IsEqualTo(1);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(OneSecond);
        timer.Dispose();
    }

    /// <summary>A callback that re-arms its timer with <c>Change</c> while an outer <c>Change</c> fires it at
    /// once (run now, then again in a second) publishes the re-armed schedule, which the inner timer carries; the
    /// outer change, which ends later, does not overwrite it.</summary>
    [Test]
    public async Task Change_CallbackReArmsDuringAFiringChange_PublishesTheReArmedSchedule(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        var fires = new Counter();
        ITimer? timer = null;
        timer = time.CreateTimer(
            _ =>
            {
                fires.Increment();
                if (fires.Value == 1)
                {
                    timer!.Change(OneSecond, Timeout.InfiniteTimeSpan);
                }
            },
            state: null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        await Assert.That(fires.Value).IsEqualTo(1);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(OneSecond);
        time.Advance(OneSecond);
        await Assert.That(fires.Value).IsEqualTo(2);
        await Assert.That(time.NextTimerDueTime).IsNull();
        timer.Dispose();
    }

    /// <summary>A loop that creates its next timer disabled and arms it after an <c>await</c> has not armed its
    /// next wait until that change, so the settle waits for it.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_NextTimerCreatedDisabledAndArmedLater_WaitsForTheArm(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var armNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ITimer? next = null;
        var loop = Task.Run(
            async () =>
            {
                await Task.Delay(OneSecond, time, cancellationToken);
                next = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                await armNext.Task.WaitAsync(cancellationToken);
                next.Change(FiveSeconds, Timeout.InfiniteTimeSpan);
            },
            cancellationToken);
        await time.WaitForTimersCreatedAsync(1, cancellationToken);

        var settle = time.AdvanceAndSettleAsync(OneSecond, cancellationToken);
        await time.WaitForTimersCreatedAsync(2, cancellationToken);

        await Assert.That(time.TimersArmedCount).IsEqualTo(1);
        armNext.SetResult();
        await settle;
        await Assert.That(time.NextTimerDueTime).IsEqualTo(FiveSeconds);
        await loop;
        next!.Dispose();
    }

    /// <summary>A callback that disables its timer with <c>Change</c> arms nothing, so the settle waits until it
    /// is canceled.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_CallbackDisablesItsTimer_DoesNotSettle(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        ITimer? timer = null;
        timer = time.CreateTimer(
            _ => timer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan),
            state: null,
            OneSecond,
            Timeout.InfiniteTimeSpan);
        using var settle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await time.WaitUntilAsync(static provider => provider.TimerFireCount >= 1, cancellationToken);
                await settle.CancelAsync();
            },
            cancellationToken);

        var exception = await Assert.That(async () => await time.AdvanceAndSettleAsync(OneSecond, settle.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.Message).Contains("but only 0 timer(s) were armed");
        timer.Dispose();
    }

    /// <summary>A <c>Change</c> that the inner timer rejects (it returns <see langword="false"/> for a disposed
    /// timer) arms nothing, so the settle does not count it and waits until it is canceled.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_CallbackChangesADisposedTimer_DoesNotSettle(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var disposed = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        disposed.Dispose();
        var accepted = new Counter();
        var timer = time.CreateTimer(
            _ =>
            {
                if (disposed.Change(OneSecond, Timeout.InfiniteTimeSpan))
                {
                    accepted.Increment();
                }
            },
            state: null,
            OneSecond,
            Timeout.InfiniteTimeSpan);
        using var settle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await time.WaitUntilAsync(static provider => provider.TimerFireCount >= 1, cancellationToken);
                await settle.CancelAsync();
            },
            cancellationToken);

        var exception = await Assert.That(async () => await time.AdvanceAndSettleAsync(OneSecond, settle.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.Message).Contains("but only 0 timer(s) were armed");
        await Assert.That(accepted.Value).IsEqualTo(0);
        timer.Dispose();
    }

    /// <summary>A canceled token throws before the clock moves.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_PreCanceledToken_DoesNotAdvance(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => await time.AdvanceAndSettleAsync(OneSecond, cts.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    // --- AdvanceOnEachTimerUntilAsync ---

    /// <summary>A reconnect backoff with three failed attempts: the helper advances once per armed backoff and
    /// stops when the connect completes, so exactly three steps of fake time pass.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_ReconnectBackoff_AdvancesOncePerAttempt(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var attempts = new Counter();
        var connect = ConnectWithBackoffAsync(time, failures: 3, attempts, cancellationToken);

        await time.AdvanceOnEachTimerUntilAsync(connect, TimeSpan.FromSeconds(30), cancellationToken);
        await connect;

        await Assert.That(attempts.Value).IsEqualTo(4);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(90));
    }

    /// <summary>A task that has already completed ends the helper without advancing.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_AlreadyCompleted_DoesNotAdvance(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        await time.AdvanceOnEachTimerUntilAsync(Task.CompletedTask, OneSecond, cancellationToken);

        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>Canceling the token while the helper waits for a timer throws
    /// <see cref="OperationCanceledException"/>.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_CanceledWhileWaiting_Throws(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                await cts.CancelAsync();
            },
            cancellationToken);

        await Assert.That(async () =>
            {
                var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await time.AdvanceOnEachTimerUntilAsync(never.Task, OneSecond, cts.Token);
            })
            .Throws<OperationCanceledException>();
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>A null task and a non-positive step are rejected.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(null!, OneSecond, cancellationToken))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(Task.CompletedTask, TimeSpan.Zero, cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
    }

    // --- AdvanceUntilAsync ---

    /// <summary>A periodic timer: the helper advances step by step until the sixth fire, and no further.</summary>
    [Test]
    public async Task AdvanceUntilAsync_PeriodicTimer_StopsAtTheCondition(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var timer = time.CreateTimer(static _ => { }, state: null, OneSecond, OneSecond);

        await time.AdvanceUntilAsync(OneSecond, () => time.TimerFireCount >= 6, TimeSpan.FromMinutes(1), cancellationToken);

        await Assert.That(time.TimerFireCount).IsEqualTo(6);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(6));
    }

    /// <summary>A <c>Task.Delay</c> loop: each step settles, so the condition sees every iteration.</summary>
    [Test]
    public async Task AdvanceUntilAsync_DelayLoop_SettlesEachStep(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var iterations = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = DelayLoopAsync(time, OneSecond, iterations, stop.Token);

        await time.AdvanceUntilAsync(OneSecond, () => iterations.Value >= 4, TimeSpan.FromMinutes(1), cancellationToken);

        await Assert.That(iterations.Value).IsEqualTo(4);
        await stop.CancelAsync();
        await loop;
    }

    /// <summary>A condition that already holds ends the helper without advancing.</summary>
    [Test]
    public async Task AdvanceUntilAsync_ConditionAlreadyHolds_DoesNotAdvance(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        await time.AdvanceUntilAsync(OneSecond, static () => true, TimeSpan.Zero, cancellationToken);

        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>A condition that never holds fails with <see cref="TimeoutException"/> after exactly
    /// <c>maxAdvance</c> of fake time.</summary>
    [Test]
    public async Task AdvanceUntilAsync_NeverHolds_TimesOutAfterMaxAdvance(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        var exception = await Assert.That(async () =>
                await time.AdvanceUntilAsync(OneSecond, static () => false, TimeSpan.FromSeconds(3), cancellationToken))
            .Throws<TimeoutException>();

        await Assert.That(exception!.Message).Contains("did not hold after advancing 00:00:03 of fake time in steps of 00:00:01");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(3));
    }

    /// <summary>When the step does not divide the maximum, the last step is shortened: the clock never moves past
    /// <c>maxAdvance</c>, so a timer due after it does not fire.</summary>
    [Test]
    public async Task AdvanceUntilAsync_StepDoesNotDivideTheMaximum_StopsAtTheMaximum(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);

        var exception = await Assert.That(async () =>
                await time.AdvanceUntilAsync(TimeSpan.FromSeconds(2), static () => false, TimeSpan.FromSeconds(3), cancellationToken))
            .Throws<TimeoutException>();

        await Assert.That(exception!.Message).Contains("after advancing 00:00:03 of fake time");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(3));
        await Assert.That(time.TimerFireCount).IsEqualTo(0);
    }

    /// <summary>A non-positive step, a null condition, and a negative maximum are rejected.</summary>
    [Test]
    public async Task AdvanceUntilAsync_InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var max = TimeSpan.FromSeconds(1);

        await Assert.That(async () => await time.AdvanceUntilAsync(TimeSpan.Zero, static () => true, max, cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await time.AdvanceUntilAsync(OneSecond, null!, max, cancellationToken))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await time.AdvanceUntilAsync(OneSecond, static () => true, TimeSpan.FromSeconds(-1), cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
    }

    // --- loop shapes under test ---

    /// <summary>A heartbeat loop: one <c>Task.Delay</c> per iteration until <paramref name="stop"/> is canceled.</summary>
    private static async Task DelayLoopAsync(TimeProvider time, TimeSpan delay, Counter iterations, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(delay, time, stop).ConfigureAwait(false);
                iterations.Increment();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped by the test.
        }
    }

    /// <summary>A loop whose every attempt is bounded by a provider-backed <see cref="CancellationTokenSource"/>
    /// and never succeeds on its own, so each attempt ends in a timeout.</summary>
    private static async Task TimeoutSourceLoopAsync(TimeProvider time, TimeSpan timeout, Counter timeouts, CancellationToken stop)
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        while (!stop.IsCancellationRequested)
        {
            using var attempt = new CancellationTokenSource(timeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token, stop);
            try
            {
                await never.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                timeouts.Increment();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Stopped by the test.
            }
        }
    }

    /// <summary>A loop whose every attempt is bounded by <c>WaitAsync(timeout, timeProvider, ct)</c> and never
    /// succeeds on its own.</summary>
    private static async Task WaitAsyncTimeoutLoopAsync(TimeProvider time, TimeSpan timeout, Counter timeouts, CancellationToken stop)
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await never.Task.WaitAsync(timeout, time, stop).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                timeouts.Increment();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Stopped by the test.
            }
        }
    }

    /// <summary>A connect that fails <paramref name="failures"/> times, waiting a growing backoff after each
    /// failure, and then succeeds.</summary>
    private static async Task ConnectWithBackoffAsync(TimeProvider time, int failures, Counter attempts, CancellationToken cancellationToken)
    {
        var attempt = 1;
        while (attempt <= failures)
        {
            attempts.Increment();
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), time, cancellationToken).ConfigureAwait(false);
            attempt++;
        }

        attempts.Increment();
    }

    /// <summary>A thread-safe counter shared between a loop and the test.</summary>
    private sealed class Counter
    {
        private int _value;

        /// <summary>Gets the current value.</summary>
        public int Value => Volatile.Read(ref _value);

        /// <summary>Adds one.</summary>
        public void Increment() => Interlocked.Increment(ref _value);
    }
}
