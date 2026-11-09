using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>
/// The advance helpers at their edges: zero, negative and huge steps; <c>AutoAdvanceAmount</c>; a due time exactly
/// on the step; timers due at the same instant; a zero period and a disabled due time; and an <c>until</c> task that
/// has already completed, faulted or been canceled when the call starts.
/// </summary>
[Timeout(10_000)]
internal sealed class EdgeCaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    // --- steps ---

    /// <summary>A zero advance fires nothing new and settles at once; a negative one is rejected, naming the
    /// parameter, before the clock moves.</summary>
    [Test]
    public async Task AdvanceAndSettleAsync_ZeroAndNegativeDelta(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var timer = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        await time.AdvanceAndSettleAsync(TimeSpan.Zero, cancellationToken);
        await Assert.That(time.TimerFireCount).IsEqualTo(0);

        var exception = await Assert.That(async () => await time.AdvanceAndSettleAsync(TimeSpan.FromSeconds(-1), cancellationToken))
            .ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(exception!.ParamName).IsEqualTo("delta");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    /// <summary>A step so large that the clock would pass <see cref="DateTimeOffset.MaxValue"/> is rejected with an
    /// <see cref="ArgumentOutOfRangeException"/> that names the step, before the clock moves.</summary>
    [Test]
    public async Task AdvanceHelpers_StepPastTheLatestTime_RejectedNamingTheStep(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        var onEach = await Assert.That(async () =>
            {
                var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await time.AdvanceOnEachTimerUntilAsync(never.Task, TimeSpan.MaxValue, cancellationToken);
            })
            .ThrowsExactly<ArgumentOutOfRangeException>();
        var settle = await Assert.That(async () => await time.AdvanceAndSettleAsync(TimeSpan.MaxValue, cancellationToken))
            .ThrowsExactly<ArgumentOutOfRangeException>();
        var until = await Assert.That(async () => await time.AdvanceUntilAsync(TimeSpan.MaxValue, static () => false, TimeSpan.MaxValue, cancellationToken))
            .ThrowsExactly<ArgumentOutOfRangeException>();

        await Assert.That(onEach!.ParamName).IsEqualTo("step");
        await Assert.That(settle!.ParamName).IsEqualTo("delta");
        await Assert.That(until!.ParamName).IsEqualTo("step");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
        await Assert.That(time.PendingWaitCount).IsEqualTo(0);
    }

    /// <summary>Valid steps near <see cref="DateTimeOffset.MaxValue"/>: the step that would pass the latest time is
    /// rejected, naming <c>step</c>. The message says that the advance by that step from the current clock would pass
    /// it, not that the step is invalid, and the step made before it stays made.</summary>
    [Test]
    public async Task AdvanceUntilAsync_SecondStepWouldPassTheLatestTime_SaysTheAdvanceWouldPassIt(CancellationToken cancellationToken)
    {
        var nearTheEnd = DateTimeOffset.MaxValue - TimeSpan.FromSeconds(1.5);
        var time = new ObservableFakeTimeProvider(nearTheEnd);

        var exception = await Assert.That(async () =>
                await time.AdvanceUntilAsync(OneSecond, static () => false, TimeSpan.FromSeconds(10), cancellationToken))
            .ThrowsExactly<ArgumentOutOfRangeException>();

        await Assert.That(exception!.ParamName).IsEqualTo("step");
        await Assert.That(exception.Message).StartsWith(
            "An advance by 00:00:01 from 9999-12-31T23:59:59.4999999+00:00 would move the fake clock past DateTimeOffset.MaxValue.");
        await Assert.That(time.GetUtcNow()).IsEqualTo(nearTheEnd + OneSecond);
    }

    /// <summary><see cref="ObservableFakeTimeProvider.Advance"/> rejects a negative advance and one past
    /// <see cref="DateTimeOffset.MaxValue"/> the same way, naming <c>delta</c>, before the clock moves.</summary>
    [Test]
    public async Task Advance_NegativeOrPastTheLatestTime_RejectedNamingDelta(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);

        var negative = await Assert.That(() => time.Advance(TimeSpan.FromSeconds(-1))).ThrowsExactly<ArgumentOutOfRangeException>();
        var pastTheEnd = await Assert.That(() => time.Advance(TimeSpan.MaxValue)).ThrowsExactly<ArgumentOutOfRangeException>();

        await Assert.That(negative!.ParamName).IsEqualTo("delta");
        await Assert.That(pastTheEnd!.ParamName).IsEqualTo("delta");
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    // --- AutoAdvanceAmount ---

    /// <summary>With <c>AutoAdvanceAmount</c> set, every read of the clock moves it. The helpers read the clock only
    /// for a message, so a delay loop still runs one iteration per settled advance.</summary>
    [Test]
    public async Task AutoAdvanceAmount_DelayLoop_OneIterationPerSettledAdvance(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        time.Fake.AutoAdvanceAmount = TimeSpan.FromMilliseconds(100);
        var iterations = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = DelayLoopAsync(time, iterations, stop.Token);
        await time.WaitForTimersCreatedAsync(1, cancellationToken);

        for (var round = 1; round <= 3; round++)
        {
            await time.AdvanceAndSettleAsync(OneSecond, cancellationToken);
            await Assert.That(iterations.Value).IsEqualTo(round);
        }

        await stop.CancelAsync();
        await loop;
    }

    /// <summary>With <c>AutoAdvanceAmount</c> set, the helpers do not read the clock to check an advance (a read would
    /// move it): an overflowing advance is left to the fake clock, which rejects it, and the clock has not moved.</summary>
    [Test]
    public async Task AutoAdvanceAmount_OverflowingAdvance_LeftToTheFakeClockWithoutMovingIt(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        time.Fake.AutoAdvanceAmount = OneSecond;

        await Assert.That(async () => await time.AdvanceAndSettleAsync(TimeSpan.MaxValue, cancellationToken))
            .Throws<ArgumentOutOfRangeException>();

        time.Fake.AutoAdvanceAmount = TimeSpan.Zero;
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
    }

    // --- due times on the step, and at the same instant ---

    /// <summary>A delay exactly as long as the step fires on that step: three iterations take three steps, and the
    /// clock stands exactly at three steps.</summary>
    [Test]
    public async Task DueTimeExactlyOnTheStep_FiresOnThatStep(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        var result = await time.AdvanceOnEachTimerUntilAsync(ThreeDelaysAsync(time, cancellationToken), OneSecond, cancellationToken);

        await Assert.That(result).IsEqualTo(3);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(3));
    }

    /// <summary>Two loops whose delays are due at the same instant: one settled advance fires both and waits until
    /// both have armed their next delay.</summary>
    [Test]
    public async Task TwoTimersDueAtTheSameInstant_OneSettleWaitsForBoth(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var iterations = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loops = new[] { DelayLoopAsync(time, iterations, stop.Token), DelayLoopAsync(time, iterations, stop.Token) };
        await time.WaitForTimersCreatedAsync(2, cancellationToken);

        await time.AdvanceAndSettleAsync(OneSecond, cancellationToken);

        await Assert.That(iterations.Value).IsEqualTo(2);
        await Assert.That(time.TimersCreatedCount).IsEqualTo(4);
        await stop.CancelAsync();
        await Task.WhenAll(loops);
    }

    // --- periods ---

    /// <summary>A zero period is a one-shot, as for <see cref="Timer"/>: the timer fires once, is then disabled, and
    /// a settle waits for it to be re-armed like any one-shot.</summary>
    [Test]
    public async Task ZeroPeriod_IsAOneShot(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var fires = new Counter();
        using var timer = time.CreateTimer(_ => fires.Increment(), state: null, OneSecond, TimeSpan.Zero);
        using var cancelTheWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var settle = time.AdvanceAndSettleAsync(TimeSpan.FromSeconds(3), cancelTheWait.Token);

        await Assert.That(fires.Value).IsEqualTo(1);
        await Assert.That(time.NextTimerDueTime).IsNull();
        await Assert.That(settle.IsCompleted).IsFalse();
        await cancelTheWait.CancelAsync();
        _ = await Task.WhenAny(settle);
        await Assert.That(settle.IsCanceled).IsTrue();
    }

    /// <summary>A timer created with an infinite due time never fires and has no next due time; a settle with only
    /// such timers returns at once.</summary>
    [Test]
    public async Task InfiniteDueTime_NeverFiresAndSettlesAtOnce(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var timer = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, OneSecond);

        await time.AdvanceAndSettleAsync(TimeSpan.FromDays(1), cancellationToken);

        await Assert.That(time.TimerFireCount).IsEqualTo(0);
        await Assert.That(time.NextTimerDueTime).IsNull();
    }

    // --- the until task before the call ---

    /// <summary>An <c>until</c> task that has already completed, faulted or been canceled when the call starts, with
    /// a live token: the call reports that outcome at once, without advancing and without a pending wait.</summary>
    [Test]
    public async Task UntilTaskAlreadyFinished_ReportsItsOutcomeWithoutAdvancing(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await time.AdvanceOnEachTimerUntilAsync(Task.CompletedTask, OneSecond, cancellationToken);
        await Assert.That(await time.AdvanceOnEachTimerUntilAsync(Task.FromResult(5), OneSecond, cancellationToken)).IsEqualTo(5);
        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(Task.FromException(new InvalidOperationException("failed")), OneSecond, cancellationToken))
            .ThrowsExactly<InvalidOperationException>();
        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(Task.FromException<int>(new InvalidOperationException("failed")), OneSecond, cancellationToken))
            .ThrowsExactly<InvalidOperationException>();
        await Assert.That(async () => await time.AdvanceOnEachTimerUntilAsync(Task.FromCanceled(canceled.Token), OneSecond, cancellationToken))
            .ThrowsExactly<TaskCanceledException>();

        await Assert.That(time.GetUtcNow()).IsEqualTo(Start);
        await Assert.That(time.PendingWaitCount).IsEqualTo(0);
    }

    /// <summary>Awaits three one-second delays, then returns 3.</summary>
    private static async Task<int> ThreeDelaysAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(OneSecond, time, cancellationToken).ConfigureAwait(false);
        }

        return 3;
    }

    /// <summary>One-second delays, counted in <paramref name="iterations"/>, until <paramref name="stop"/> is canceled.</summary>
    private static async Task DelayLoopAsync(TimeProvider time, Counter iterations, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(OneSecond, time, stop).ConfigureAwait(false);
                iterations.Increment();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped by the test.
        }
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
