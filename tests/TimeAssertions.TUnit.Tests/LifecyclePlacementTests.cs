using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>
/// The provider, the helpers and the assertions where users call them: in the test class constructor, in
/// <c>[Before(Class)]</c> / <c>[After(Class)]</c> and <c>[Before(Test)]</c> / <c>[After(Test)]</c> hooks, on a retried
/// attempt, and when <c>[Timeout]</c> ends a token-only wait. TUnit runs hooks and fixtures inside the first test's
/// context; the library reads no test context, so each place must work the same.
/// </summary>
[Timeout(15_000)]
[NotInParallel(nameof(LifecyclePlacementTests))]
internal sealed class LifecyclePlacementTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TaskCompletionSource<string> TimedOutAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ObservableFakeTimeProvider? s_classClock;
    private static int s_retriedAttempts;
    private static int s_timeoutAttempts;

    private readonly ObservableFakeTimeProvider _constructorClock = new(Start);
    private readonly CancellationTokenSource _stopHookLoop = new();
    private ObservableFakeTimeProvider? _hookClock;
    private int _hookLoopIterations;

    /// <summary>A clock created in <c>[Before(Class)]</c>, with a settled advance there.</summary>
    [Before(Class)]
    public static async Task CreateTheClassClock(CancellationToken cancellationToken)
    {
        s_classClock = new ObservableFakeTimeProvider(Start);
        ITimer? timer = null;
        timer = s_classClock.CreateTimer(state => _ = timer!.Change(OneSecond, Timeout.InfiniteTimeSpan), state: null, OneSecond, Timeout.InfiniteTimeSpan);
        await s_classClock.AdvanceAndSettleAsync(OneSecond, cancellationToken);
    }

    /// <summary>The class clock asserted in <c>[After(Class)]</c>.</summary>
    [After(Class)]
    public static async Task CheckTheClassClock(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(s_classClock!).HasTimerFiredCount(1);
        await Assert.That(s_classClock!).HasTimerScheduled(OneSecond);
    }

    /// <summary>A delay loop started in <c>[Before(Test)]</c>, waited for there.</summary>
    [Before(Test)]
    public async Task StartTheHookLoop(CancellationToken cancellationToken)
    {
        _hookClock = new ObservableFakeTimeProvider(Start);
        _ = HookLoopAsync(_hookClock, _stopHookLoop.Token);
        await _hookClock.WaitForTimersCreatedAsync(1, cancellationToken);
    }

    /// <summary>The loop stopped in <c>[After(Test)]</c>; an "eventually" assertion waits until its delay's timer is
    /// gone.</summary>
    [After(Test)]
    public async Task StopTheHookLoopAndCheckForLeaks(CancellationToken cancellationToken)
    {
        await _stopHookLoop.CancelAsync();
        await Assert.That(_hookClock!).HasNoActiveTimersEventually(TimeSpan.FromSeconds(2), cancellationToken);
    }

    /// <summary>The test body drives the loop that the <c>[Before(Test)]</c> hook started.</summary>
    [Test]
    public async Task Body_DrivesTheLoopFromTheBeforeTestHook(CancellationToken cancellationToken)
    {
        for (var round = 1; round <= 3; round++)
        {
            await _hookClock!.AdvanceAndSettleAsync(OneSecond, cancellationToken);
        }

        await Assert.That(Volatile.Read(ref _hookLoopIterations)).IsEqualTo(3);
    }

    /// <summary>A clock created in the constructor is the test's own: fresh, and usable in the body.</summary>
    [Test]
    public async Task ConstructorClock_IsFreshAndUsable(CancellationToken cancellationToken)
    {
        await Assert.That(_constructorClock.TimersCreatedCount).IsEqualTo(0);
        var delay = Task.Delay(OneSecond, _constructorClock, cancellationToken);
        await _constructorClock.AdvanceOnEachTimerUntilAsync(delay, OneSecond, cancellationToken);
        await Assert.That(_constructorClock).HasNoActiveTimers();
    }

    /// <summary>A retried attempt gets a new instance, so a constructor clock starts fresh again after a failed
    /// attempt that created timers.</summary>
    [Test]
    [Retry(1)]
    public async Task RetriedAttempt_ConstructorClockStartsFresh(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(_constructorClock.TimersCreatedCount).IsEqualTo(0);
        _ = _constructorClock.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        if (Interlocked.Increment(ref s_retriedAttempts) == 1)
        {
            throw new InvalidOperationException("first attempt fails on purpose, after creating a timer");
        }
    }

    /// <summary><c>[Timeout]</c> ends a token-only "eventually" wait: the first attempt's cancellation says what it
    /// waited for and what remained. The second attempt checks that message; it waits for it, since TUnit may start
    /// the retry before the first attempt's body has seen its canceled token.</summary>
    [Test]
    [Timeout(1_000)]
    [Retry(1)]
    public async Task TimeoutEndsATokenOnlyWait_TheCancellationSaysWhy(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref s_timeoutAttempts) == 1)
        {
            _ = _constructorClock.CreateTimer(static _ => { }, state: null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
            try
            {
                await Assert.That(_constructorClock).HasNoActiveTimersEventually(cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                TimedOutAttempt.TrySetResult(exception.Message);
                throw;
            }
        }

        var message = await TimedOutAttempt.Task.WaitAsync(cancellationToken);
        await Assert.That(message).StartsWith("Canceled while waiting for no active timers: 1 remained (count=1):");
    }

    /// <inheritdoc/>
    public void Dispose() => _stopHookLoop.Dispose();

    /// <summary>The loop the hook starts: one-second delays, counted, until <paramref name="stop"/> is canceled.</summary>
    private async Task HookLoopAsync(TimeProvider time, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(OneSecond, time, stop).ConfigureAwait(false);
                _ = Interlocked.Increment(ref _hookLoopIterations);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped by the hook.
        }
    }
}
