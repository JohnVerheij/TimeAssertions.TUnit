using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>
/// The provider under concurrent use, as background services use it: timers created, changed and disposed from
/// several threads while another thread advances the clock; a <c>Change</c> racing a fire; a disposal racing a fire;
/// and two advance helpers on one provider at once. Each test checks invariants that must hold whatever the
/// interleaving: every creation and disposal counted once, every fire counted once, nothing left active.
/// </summary>
[Timeout(30_000)]
internal sealed class ConcurrencyTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);

    /// <summary>Eight threads each create, change and dispose 250 timers (one-shot and periodic, some due at once)
    /// while another thread advances the clock: the counts match what happened, and nothing stays active.</summary>
    [Test]
    public async Task ManyThreadsCreateChangeAndDispose_WhileTheClockAdvances_TheCountsMatch(CancellationToken cancellationToken)
    {
        const int workers = 8;
        const int timersPerWorker = 250;
        var time = new ObservableFakeTimeProvider(Start);
        var callbacks = new Counter();
        using var stopAdvancing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var advancing = AdvanceUntilStoppedAsync(time, stopAdvancing.Token);

        var work = Enumerable.Range(0, workers).Select(worker => Task.Run(
            () =>
            {
                for (var i = 0; i < timersPerWorker; i++)
                {
                    var period = i % 3 == 0 ? TimeSpan.FromMilliseconds(4) : Timeout.InfiniteTimeSpan;
                    var timer = time.CreateTimer(_ => callbacks.Increment(), state: null, TimeSpan.FromMilliseconds((i + worker) % 5), period);
                    if (i % 2 == 0)
                    {
                        _ = timer.Change(TimeSpan.FromMilliseconds(i % 4), period);
                    }

                    timer.Dispose();
                }
            },
            cancellationToken));
        await Task.WhenAll(work);
        await stopAdvancing.CancelAsync();
        await advancing;

        await Assert.That(time.TimersCreatedCount).IsEqualTo(workers * timersPerWorker);
        await Assert.That(time.TimersDisposedCount).IsEqualTo(workers * timersPerWorker);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
        await Assert.That(time.NextTimerDueTime).IsNull();
        await Assert.That(time.TimerFireCount).IsEqualTo(callbacks.Value);
    }

    /// <summary>One periodic timer changed 2,000 times from one thread while another advances the clock: every fire
    /// is counted once, and after a last change the published schedule is that change's.</summary>
    [Test]
    public async Task ChangeRacingFires_ThePublishedScheduleIsTheLastChange(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var callbacks = new Counter();
        using var timer = time.CreateTimer(_ => callbacks.Increment(), state: null, OneMillisecond, OneMillisecond);
        using var stopAdvancing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var advancing = AdvanceUntilStoppedAsync(time, stopAdvancing.Token);

        await Task.Run(
            () =>
            {
                for (var i = 0; i < 2_000; i++)
                {
                    _ = timer.Change(TimeSpan.FromMilliseconds(i % 3), TimeSpan.FromMilliseconds((i % 4) + 1));
                }
            },
            cancellationToken);
        await stopAdvancing.CancelAsync();
        await advancing;
        await Assert.That(time.ActiveTimers[0].Period).IsEqualTo(TimeSpan.FromMilliseconds(4))
            .Because("the last racing change (i = 1999) set a period of 4 ms");
        _ = timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        await Assert.That(time.ActiveTimers[0].DueTime).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(time.ActiveTimers[0].Period).IsEqualTo(Timeout.InfiniteTimeSpan);
        await Assert.That(time.TimerFireCount).IsEqualTo(callbacks.Value);
    }

    /// <summary>500 timers due within a millisecond, disposed from one thread while another advances the clock in
    /// one-millisecond steps: each disposal is counted once, no timer stays active, and every fire is counted once.</summary>
    [Test]
    public async Task DisposeRacingFires_EachDisposalCountedOnceAndNothingActive(CancellationToken cancellationToken)
    {
        const int timerCount = 500;
        var time = new ObservableFakeTimeProvider(Start);
        var callbacks = new Counter();
        var timers = Enumerable.Range(0, timerCount)
            .Select(i => time.CreateTimer(_ => callbacks.Increment(), state: null, OneMillisecond, i % 2 == 0 ? OneMillisecond : Timeout.InfiniteTimeSpan))
            .ToArray();
        using var stopAdvancing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var advancing = AdvanceUntilStoppedAsync(time, stopAdvancing.Token);

        await Task.Run(
            () =>
            {
                foreach (var timer in timers)
                {
                    timer.Dispose();
                }
            },
            cancellationToken);
        await stopAdvancing.CancelAsync();
        await advancing;

        await Assert.That(time.TimersDisposedCount).IsEqualTo(timerCount);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
        await Assert.That(time.TimerFireCount).IsEqualTo(callbacks.Value);
    }

    /// <summary>Two <c>AdvanceOnEachTimerUntilAsync</c> calls on one provider at once, each driving its own backoff
    /// loop (as two parallel tests sharing a clock would): both complete with their loop's result, and no wait is
    /// left pending.</summary>
    [Test]
    public async Task TwoAdvanceHelpersOnOneProvider_BothComplete(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);

        var first = time.AdvanceOnEachTimerUntilAsync(BackoffAsync(time, failures: 3, cancellationToken), TimeSpan.FromSeconds(15), cancellationToken);
        var second = time.AdvanceOnEachTimerUntilAsync(BackoffAsync(time, failures: 2, cancellationToken), TimeSpan.FromSeconds(15), cancellationToken);
        var results = await Task.WhenAll(first, second);

        await Assert.That(results).IsEquivalentTo([4, 3]);
        await Assert.That(time.PendingWaitCount).IsEqualTo(0);
    }

    /// <summary>Two settles on one provider at once, each after its own advance, with two delay loops: both return,
    /// the clock moved by both advances, and each loop has armed its next delay.</summary>
    [Test]
    public async Task TwoSettlesOnOneProvider_BothReturn(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loops = new[] { DelayLoopAsync(time, stop.Token), DelayLoopAsync(time, stop.Token) };
        await time.WaitForTimersCreatedAsync(2, cancellationToken);

        await Task.WhenAll(
            Task.Run(() => time.AdvanceAndSettleAsync(TimeSpan.FromSeconds(1), cancellationToken), cancellationToken),
            Task.Run(() => time.AdvanceAndSettleAsync(TimeSpan.FromSeconds(1), cancellationToken), cancellationToken));

        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(2));
        await time.WaitUntilAsync(static provider => provider.ActiveTimerCount == 2 && provider.NextTimerDueTime == TimeSpan.FromSeconds(1), cancellationToken);
        await stop.CancelAsync();
        await Task.WhenAll(loops);
    }

    private static async Task AdvanceUntilStoppedAsync(ObservableFakeTimeProvider time, CancellationToken stop)
    {
        await Task.Yield();
        while (!stop.IsCancellationRequested)
        {
            time.Advance(OneMillisecond);
            await Task.Yield();
        }
    }

    private static async Task<int> BackoffAsync(TimeProvider time, int failures, CancellationToken cancellationToken)
    {
        await Task.Yield();
        for (var attempt = 1; attempt <= failures; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), time, cancellationToken).ConfigureAwait(false);
        }

        return failures + 1;
    }

    private static async Task DelayLoopAsync(TimeProvider time, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), time, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped by the test.
        }
    }

    /// <summary>A thread-safe counter shared between callbacks and the test.</summary>
    private sealed class Counter
    {
        private int _value;

        /// <summary>Gets the current value.</summary>
        public int Value => Volatile.Read(ref _value);

        /// <summary>Adds one.</summary>
        public void Increment() => Interlocked.Increment(ref _value);
    }
}
