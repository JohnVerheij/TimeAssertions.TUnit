using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;
using TUnit.Assertions.Exceptions;

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

    /// <summary>The cancellation message of a one-second settle whose fired one-shot timer armed nothing.</summary>
    private const string OneAdvanceUnsettled =
        "Advanced 1 time(s) by 00:00:01, 00:00:01 in total (clock now 2026-01-01T00:00:01.0000000+00:00): the last advance fired 1 one-shot timer(s), but only 0 timer(s) were armed (created with a due time, or re-armed with Change; moving a pending timer is not an arm) before the wait was canceled. A loop that ends after its timer fires arms no new one; for a periodic timer use AdvanceUntilAsync.";

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
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(OneAdvanceUnsettled);
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
        await Assert.That(settle.IsCompleted).IsFalse();
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
        await Assert.That(exception!.Message).IsEqualTo(OneAdvanceUnsettled);
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
        await Assert.That(exception!.Message).IsEqualTo(OneAdvanceUnsettled);
        await Assert.That(accepted.Value).IsEqualTo(0);
        timer.Dispose();
    }

    /// <summary>A callback that re-arms and cancels the token during the advance ends the settle with its counts met
    /// (the wait checks the token first): the cancellation reports the counts without a shortfall or the loop
    /// hint.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_CanceledDuringTheAdvanceWithTheCountsMet_ReportsNoShortfall(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void ReArmAndCancel()
        {
            _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
            cts.Cancel();
        }

        _ = time.CreateTimer(_ => ReArmAndCancel(), state: null, OneSecond, Timeout.InfiniteTimeSpan);

        var exception = await Assert.That(async () => await time.AdvanceAndSettleAsync(OneSecond, cts.Token))
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 1 time(s) by 00:00:01, 00:00:01 in total (clock now 2026-01-01T00:00:01.0000000+00:00): the last advance fired 1 one-shot timer(s), and 1 timer(s) were armed, but the token was canceled before the settle returned.");
        await Assert.That(exception.CancellationToken).IsEqualTo(cts.Token);
    }

    /// <summary>A canceled token throws before the clock moves, and says so.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_PreCanceledToken_DoesNotAdvance(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.That(async () => await time.AdvanceAndSettleAsync(OneSecond, cts.Token))
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 0 time(s) by 00:00:01, 00:00:00 in total (clock now 2026-01-01T00:00:00.0000000+00:00), but the token was canceled before the first advance.");
        await Assert.That(exception.CancellationToken).IsEqualTo(cts.Token);
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

        await Assert.That(attempts.Value).IsEqualTo(4);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(90));
    }

    /// <summary>The <see cref="Task{TResult}"/> form returns the task's result once it completes.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_TaskWithResult_ReturnsTheResult(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        var attempts = await time.AdvanceOnEachTimerUntilAsync(
            CountAttemptsWithBackoffAsync(time, failures: 3, cancellationToken), TimeSpan.FromSeconds(30), cancellationToken);

        await Assert.That(attempts).IsEqualTo(4);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(90));
    }

    /// <summary>A task that faults after some advances fails the helper with the task's own exception.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_TaskFaultsAfterAdvances_ThrowsTheFault(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        var exception = await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(
                GiveUpAfterBackoffAsync(time, failures: 3, cancellationToken), TimeSpan.FromSeconds(30), cancellationToken))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(exception!.Message).IsEqualTo("gave up after 3 attempt(s)");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(90));
    }

    /// <summary>The <see cref="Task{TResult}"/> form rethrows a fault the same way.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_TaskWithResultFaults_ThrowsTheFault(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        var exception = await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(
                GiveUpWithResultAfterBackoffAsync(time, failures: 2, cancellationToken), TimeSpan.FromSeconds(30), cancellationToken))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(exception!.Message).IsEqualTo("gave up after 2 attempt(s)");
    }

    /// <summary>A task that has already faulted fails the helper at once, without advancing.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_AlreadyFaulted_ThrowsWithoutAdvancing(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(
                Task.FromException(new InvalidOperationException("gave up")), OneSecond, cancellationToken))
            .ThrowsExactly<InvalidOperationException>();
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>A canceled task fails the helper with the task's own cancellation, not with the helper's.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_TaskCanceled_ThrowsItsCancellation(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var taskSource = new CancellationTokenSource();
        await taskSource.CancelAsync();

        var exception = await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(
                Task.FromCanceled(taskSource.Token), OneSecond, cancellationToken))
            .ThrowsExactly<TaskCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(taskSource.Token);
    }

    /// <summary>A task that faulted with several exceptions fails the helper with the first, as <c>await</c> does.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_SeveralFaults_ThrowsTheFirst(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        var exception = await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(
                Task.WhenAll(
                    Task.FromException(new InvalidOperationException("first")),
                    Task.FromException(new InvalidOperationException("second"))),
                OneSecond,
                cancellationToken))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(exception!.Message).IsEqualTo("first");
    }

    /// <summary>A failing assertion's task passed as the task fails the helper with that assertion's exception, so
    /// an assertion cannot pass unnoticed. The assertion is awaited where it is written (TUnitAssertions0002), in a
    /// local function whose task is passed.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_FailingAssertionTask_ThrowsTheAssertionException(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var created = time.TimersCreatedCount;

        async Task CreatedOneAsync() => await Assert.That(created).IsEqualTo(1);

        var exception = await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(CreatedOneAsync(), OneSecond, cancellationToken))
            .Throws<AssertionException>();

        await Assert.That(exception!.Message).StartsWith("Expected to be 1");
    }

    /// <summary>A task that completes while the helper waits for a timer ends the helper with the task's outcome,
    /// not with a cancellation.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_TaskCompletesWhileWaitingForATimer_Completes(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var advancing = time.AdvanceOnEachTimerUntilAsync(connected.Task, OneSecond, cancellationToken);
        await Assert.That(advancing.IsCompleted).IsFalse();
        connected.SetResult();
        await advancing;

        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>The wait for the next timer ends with the call: when the task completes while that wait is pending,
    /// no wait is left registered on the provider once the call has returned.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_TaskCompletesWhileWaitingForATimer_LeavesNoWaitPending(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var advancing = time.AdvanceOnEachTimerUntilAsync(connected.Task, OneSecond, cancellationToken);
        await Assert.That(time.PendingWaitCount).IsEqualTo(1);
        connected.SetResult();
        await advancing;

        await Assert.That(time.PendingWaitCount).IsEqualTo(0);
    }

    /// <summary>When the task has completed by the time the token is canceled, the task's outcome is reported:
    /// its fault, or its result.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_TokenCanceledAfterTheTaskCompleted_ReportsTheTaskOutcome(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(
                Task.FromException(new InvalidOperationException("gave up")), OneSecond, cts.Token))
            .ThrowsExactly<InvalidOperationException>();
        await Assert.That(await time.AdvanceOnEachTimerUntilAsync(Task.FromResult(7), OneSecond, cts.Token)).IsEqualTo(7);
    }

    /// <summary>Canceling the token before any timer exists says that no advance was made and no timer was
    /// created, and keeps the canceled wait as the inner exception.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_CanceledBeforeAnyTimer_SaysNoAdvanceWasMade(CancellationToken cancellationToken)
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

        var exception = await Assert.That(async () =>
            {
                var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await time.AdvanceOnEachTimerUntilAsync(never.Task, OneSecond, cts.Token);
            })
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 0 time(s) by 00:00:01, 00:00:00 in total (clock now 2026-01-01T00:00:00.0000000+00:00), but the task had not completed when the wait was canceled: the first advance waited for timer 1, and 0 timer(s) were created.");
        await Assert.That(exception.CancellationToken).IsEqualTo(cts.Token);
        await Assert.That(exception.InnerException).IsAssignableTo<OperationCanceledException>();
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>A token that is already canceled stops the call before the first advance, even when a timer
    /// exists; the cancellation counts that timer.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_PreCanceledTokenWithATimer_CountsTheTimer(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.That(async () =>
            {
                var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await time.AdvanceOnEachTimerUntilAsync(never.Task, OneSecond, cts.Token);
            })
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 0 time(s) by 00:00:01, 00:00:00 in total (clock now 2026-01-01T00:00:00.0000000+00:00), but the task had not completed when the wait was canceled: the first advance waited for timer 1, and 1 timer(s) were created.");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>The <see cref="Task{TResult}"/> form rethrows the cancellation of its task, as awaiting it would.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_CanceledTaskWithResult_RethrowsItsCancellation(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.That(async () =>
                await time.AdvanceOnEachTimerUntilAsync(Task.FromCanceled<int>(canceled.Token), OneSecond, cancellationToken))
            .ThrowsExactly<TaskCanceledException>();
    }

    /// <summary>Canceling the token after some advances names the advances, the total, the clock, and the timer
    /// counts.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_CanceledAfterAdvances_SaysHowFarItAdvanced(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fires = new Counter();

        // Each fire arms the next one-second timer; the third fire cancels instead, so the loop stops creating timers.
        void Arm() => _ = time.CreateTimer(
            _ =>
            {
                fires.Increment();
                if (fires.Value < 3)
                {
                    Arm();
                }
                else
                {
                    cts.Cancel();
                }
            },
            state: null,
            OneSecond,
            Timeout.InfiniteTimeSpan);
        Arm();

        var exception = await Assert.That(async () =>
            {
                var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await time.AdvanceOnEachTimerUntilAsync(never.Task, OneSecond, cts.Token);
            })
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 3 time(s) by 00:00:01, 00:00:03 in total (clock now 2026-01-01T00:00:03.0000000+00:00), but the task had not completed when the wait was canceled: the next advance waited for timer 4, and 3 timer(s) were created. A loop in a wait longer than the step is not woken by an advance, so it creates no new timer.");
        await Assert.That(exception.CancellationToken).IsEqualTo(cts.Token);
        await Assert.That(time.PendingWaitCount).IsEqualTo(0);
    }

    /// <summary>A null task and a non-positive step are rejected, in both forms.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(null!, OneSecond, cancellationToken))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(Task.CompletedTask, TimeSpan.Zero, cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(default(Task<int>)!, OneSecond, cancellationToken))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(Task.FromResult(1), TimeSpan.Zero, cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>The call shapes a caller writes compile and bind as intended: a <see cref="Task{TResult}"/> argument
    /// binds the generic form, a <see cref="Task"/> the other, and method groups and async lambdas both work.</summary>
    [Test]
    public async Task AdvanceOnEachTimerUntilAsync_CallShapes_Bind(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        Func<Task, TimeSpan, CancellationToken, Task> withoutResult = time.AdvanceOnEachTimerUntilAsync;
        Func<Task<int>, TimeSpan, CancellationToken, Task<int>> withResult = time.AdvanceOnEachTimerUntilAsync;
        Func<Task<int>, TimeSpan, CancellationToken, Task> withResultAsTask = time.AdvanceOnEachTimerUntilAsync;
        Func<Task> lambda = () => time.AdvanceOnEachTimerUntilAsync(Task.FromResult(1), OneSecond, cancellationToken);

        await withoutResult(Task.CompletedTask, OneSecond, cancellationToken);
        await Assert.That(await withResult(Task.FromResult(3), OneSecond, cancellationToken)).IsEqualTo(3);
        await withResultAsTask(Task.FromResult(4), OneSecond, cancellationToken);
        await lambda();
        await Assert.That(await time.AdvanceOnEachTimerUntilAsync(
            Task.Run(async () => await CountAttemptsWithBackoffAsync(time, failures: 1, cancellationToken), cancellationToken),
            TimeSpan.FromSeconds(30),
            cancellationToken)).IsEqualTo(2);
        await Assert.That(await time.AdvanceOnEachTimerUntilAsync(
            Task.WhenAll(Task.FromResult(1), Task.FromResult(2)), OneSecond, cancellationToken)).IsEquivalentTo([1, 2]);
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

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 3 time(s) by 00:00:01, 00:00:03 in total (clock now 2026-01-01T00:00:03.0000000+00:00), but the condition did not hold within the maximum advance of 00:00:03.");
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

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 2 time(s) by up to 00:00:02, 00:00:03 in total (clock now 2026-01-01T00:00:03.0000000+00:00), but the condition did not hold within the maximum advance of 00:00:03.");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(3));
        await Assert.That(time.TimerFireCount).IsEqualTo(0);
    }

    /// <summary>A canceled settle inside the helper names the helper's own advances so far, not only the last one.</summary>
    [Test]
    public async Task AdvanceUntilAsync_SettleCanceled_SaysHowFarItAdvanced(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var settle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var finalRun = Task.Delay(TimeSpan.FromSeconds(2), time, cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await time.WaitUntilAsync(static provider => provider.TimerFireCount >= 1, cancellationToken);
                await settle.CancelAsync();
            },
            cancellationToken);

        var exception = await Assert.That(async () =>
                await time.AdvanceUntilAsync(OneSecond, static () => false, TimeSpan.FromSeconds(5), settle.Token))
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 2 time(s) by 00:00:01, 00:00:02 in total (clock now 2026-01-01T00:00:02.0000000+00:00): the last advance fired 1 one-shot timer(s), but only 0 timer(s) were armed (created with a due time, or re-armed with Change; moving a pending timer is not an arm) before the wait was canceled. A loop that ends after its timer fires arms no new one.");
        await finalRun;
    }

    /// <summary>A token canceled between two steps (here by the condition) stops the helper before the next
    /// advance, and the cancellation names the advances made so far.</summary>
    [Test]
    public async Task AdvanceUntilAsync_CanceledBetweenSteps_SaysHowFarItAdvanced(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var checks = new Counter();

        bool CancelOnThirdCheck()
        {
            checks.Increment();
            if (checks.Value == 3)
            {
                cts.Cancel();
            }

            return false;
        }

        var exception = await Assert.That(async () =>
                await time.AdvanceUntilAsync(OneSecond, CancelOnThirdCheck, TimeSpan.FromSeconds(5), cts.Token))
            .ThrowsExactly<OperationCanceledException>();

        await Assert.That(exception!.Message).IsEqualTo(
            "Advanced 2 time(s) by 00:00:01, 00:00:02 in total (clock now 2026-01-01T00:00:02.0000000+00:00), but the token was canceled before the next advance.");
        await Assert.That(exception.CancellationToken).IsEqualTo(cts.Token);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(2));
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

    /// <summary>A connect like <see cref="ConnectWithBackoffAsync"/> that returns its number of attempts.</summary>
    private static async Task<int> CountAttemptsWithBackoffAsync(TimeProvider time, int failures, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= failures; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), time, cancellationToken).ConfigureAwait(false);
        }

        return failures + 1;
    }

    /// <summary>A connect that waits a growing backoff after each of <paramref name="failures"/> failed attempts
    /// and then gives up.</summary>
    private static async Task GiveUpAfterBackoffAsync(TimeProvider time, int failures, CancellationToken cancellationToken)
    {
        _ = await GiveUpWithResultAfterBackoffAsync(time, failures, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The <see cref="Task{TResult}"/> form of <see cref="GiveUpAfterBackoffAsync"/>.</summary>
    private static async Task<int> GiveUpWithResultAfterBackoffAsync(TimeProvider time, int failures, CancellationToken cancellationToken)
    {
        await CountAttemptsWithBackoffAsync(time, failures, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"gave up after {failures} attempt(s)");
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
