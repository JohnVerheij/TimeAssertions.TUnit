using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>
/// The advance helpers and the timer counts against the timer users production code has, beyond
/// <c>CreateTimer</c>: <see cref="PeriodicTimer"/>, <c>CancellationTokenSource</c> timeouts and <c>CancelAfter</c>,
/// <c>WaitAsync</c> and <c>Task.Delay</c> that end early, and callbacks that change or dispose their own timer.
/// </summary>
[Timeout(10_000)]
internal sealed class RealTimerUsersTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    // --- PeriodicTimer ---

    /// <summary>A <see cref="PeriodicTimer"/> on the provider is one periodic timer: <c>AdvanceUntilAsync</c> over
    /// the fire count stops at the third tick, and disposing the periodic timer disposes its timer.</summary>
    [Test]
    public async Task PeriodicTimer_AdvanceUntilTheFireCount_StopsAtTheThirdTick(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var periodic = new PeriodicTimer(OneSecond, time);

        await Assert.That(time.TimersCreatedCount).IsEqualTo(1);
        await time.AdvanceUntilAsync(OneSecond, () => time.TimerFireCount >= 3, TimeSpan.FromSeconds(10), cancellationToken);
        await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(3));

        periodic.Dispose();
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
        await Assert.That(time.TimersDisposedCount).IsEqualTo(1);
    }

    /// <summary>A <see cref="PeriodicTimer"/> loop that awaits other work after each tick: <c>AdvanceUntilAsync</c>
    /// over the fire count, the condition the docs recommend, stops the clock at exactly the third tick, whatever the
    /// loop is doing, and the loop has processed no more ticks than fired. How far the loop's own count is behind
    /// depends on scheduling, so the test does not pin it.</summary>
    [Test]
    public async Task PeriodicTimerLoopWithAsyncWork_AdvanceUntilTheFireCount_StopsAtTheThirdTick(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var processed = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var periodic = new PeriodicTimer(OneSecond, time);
        try
        {
            var loop = TickLoopWithWorkAsync(periodic, processed, stop.Token);

            await time.AdvanceUntilAsync(OneSecond, () => time.TimerFireCount >= 3, TimeSpan.FromSeconds(10), cancellationToken);

            await Assert.That(time.GetUtcNow()).IsEqualTo(Start + TimeSpan.FromSeconds(3));
            await stop.CancelAsync();
            await loop;
            await Assert.That(processed.Value).IsLessThanOrEqualTo(3);
        }
        finally
        {
            periodic.Dispose();
        }
    }

    // --- CancellationTokenSource timeouts ---

    /// <summary>A loop that extends a watchdog with <c>CancelAfter</c> on every iteration (one <c>Change</c> of the
    /// watchdog's timer) before it waits again with <c>Task.Delay</c>. After each settled advance the loop has
    /// finished its iteration and armed its next delay.</summary>
    [Test]
    public async Task WatchdogLoop_ExtendsItsDeadlineEachIteration_EachSettleEndsAfterTheIteration(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var iterations = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = WatchdogLoopAsync(time, iterations, stop.Token);
        await time.WaitForTimersCreatedAsync(2, cancellationToken);

        for (var round = 1; round <= 50; round++)
        {
            var createdBefore = time.TimersCreatedCount;
            await time.AdvanceAndSettleAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            await Assert.That(time.TimersCreatedCount).IsEqualTo(createdBefore + 1)
                .Because($"after settle {round} the loop must have created its next delay");
            await Assert.That(iterations.Value).IsEqualTo(round);
        }

        await stop.CancelAsync();
        await loop;
    }

    /// <summary>A provider-backed source releases its timer when it is disposed before its timeout, and also when it
    /// is canceled by hand: the timer is disposed and never fires.</summary>
    [Test]
    public async Task CancellationTokenSource_DisposedOrCanceledEarly_ReleasesItsTimer(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);

        var disposed = new CancellationTokenSource(TimeSpan.FromMinutes(1), time);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        disposed.Dispose();
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);

        using var canceled = new CancellationTokenSource(TimeSpan.FromMinutes(1), time);
        await canceled.CancelAsync();
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
        await Assert.That(time.TimersDisposedCount).IsEqualTo(2);
        time.Advance(TimeSpan.FromMinutes(2));
        await Assert.That(time.TimerFireCount).IsEqualTo(0);
    }

    // --- WaitAsync and Task.Delay that end early ---

    /// <summary><c>WaitAsync(timeout, provider)</c> whose task completes first disposes its timeout timer, and a
    /// <c>Task.Delay</c> canceled by its token disposes its delay timer: neither leaks.</summary>
    [Test]
    public async Task WaitAsyncAndDelay_EndedEarly_LeaveNoActiveTimer(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var waiting = done.Task.WaitAsync(TimeSpan.FromMinutes(1), time, cancellationToken);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        done.SetResult();
        await waiting;
        await time.WaitForTimersDisposedAsync(1, cancellationToken);

        using var cancelDelay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(TimeSpan.FromMinutes(1), time, cancelDelay.Token);
        await cancelDelay.CancelAsync();
        _ = await Task.WhenAny(delay);
        await Assert.That(delay.IsCanceled).IsTrue();
        await time.WaitForTimersDisposedAsync(2, cancellationToken);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
    }

    // --- callbacks that change or dispose their own timer ---

    /// <summary>A periodic timer whose callback stops it with <c>Change(Infinite, Infinite)</c> after two fires:
    /// it stays active (not disposed) but has no next due time, and a later <c>Change</c> arms it again.</summary>
    [Test]
    public async Task PeriodicCallback_StopsItselfWithChange_IsActiveButNotDue(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        var fires = new Counter();
        ITimer? timer = null;
        timer = time.CreateTimer(
            state =>
            {
                fires.Increment();
                if (fires.Value == 2)
                {
                    _ = timer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
            },
            state: null,
            OneSecond,
            OneSecond);

        time.Advance(TimeSpan.FromSeconds(5));
        await Assert.That(fires.Value).IsEqualTo(2);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        await Assert.That(time.NextTimerDueTime).IsNull();

        _ = timer.Change(OneSecond, Timeout.InfiniteTimeSpan);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(OneSecond);
        time.Advance(OneSecond);
        await Assert.That(fires.Value).IsEqualTo(3);
        timer.Dispose();
    }

    /// <summary>A one-shot timer whose callback disposes it: the fire and the disposal are both counted, and the
    /// timer is gone.</summary>
    [Test]
    public async Task OneShotCallback_DisposesItsTimer_IsCountedAndGone(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        ITimer? timer = null;
        timer = time.CreateTimer(_ => timer!.Dispose(), state: null, OneSecond, Timeout.InfiniteTimeSpan);

        time.Advance(OneSecond);

        await Assert.That(time.TimerFireCount).IsEqualTo(1);
        await Assert.That(time.TimersDisposedCount).IsEqualTo(1);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
    }

    /// <summary>A periodic timer whose callback shortens its own period: the new period applies from the next
    /// fire on.</summary>
    [Test]
    public async Task PeriodicCallback_ShortensItsPeriod_TheNewPeriodApplies(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableFakeTimeProvider(Start);
        var fires = new Counter();
        ITimer? timer = null;
        timer = time.CreateTimer(
            state =>
            {
                fires.Increment();
                if (fires.Value == 1)
                {
                    _ = timer!.Change(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
                }
            },
            state: null,
            OneSecond,
            OneSecond);

        time.Advance(OneSecond);
        await Assert.That(fires.Value).IsEqualTo(1);
        await Assert.That(time.ActiveTimers[0].Period).IsEqualTo(TimeSpan.FromMilliseconds(100));
        time.Advance(TimeSpan.FromMilliseconds(300));
        await Assert.That(fires.Value).IsEqualTo(4);
        timer.Dispose();
    }

    private static async Task TickLoopWithWorkAsync(PeriodicTimer periodic, Counter processed, CancellationToken stop)
    {
        try
        {
            while (await periodic.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                await Task.Yield();
                processed.Increment();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped by the test.
        }
    }

    private static async Task WatchdogLoopAsync(TimeProvider time, Counter iterations, CancellationToken stop)
    {
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), time, stop).ConfigureAwait(false);
                watchdog.CancelAfter(TimeSpan.FromSeconds(5));
                await Task.Yield();
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
