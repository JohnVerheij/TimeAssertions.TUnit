using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>
/// Tests for the event-driven side of <see cref="ObservableTimeProvider"/>: the monotonic creation and disposal
/// counters, the waits (<see cref="ObservableTimeProvider.WaitUntilAsync"/> and the count waits), and the order
/// in which a timer change becomes visible relative to the inner provider. No test waits on the real clock: a
/// wait either completes on a change the test makes, or the test cancels it.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class ObservableTimeProviderWaitTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FiveSeconds = TimeSpan.FromSeconds(5);

    // --- counters ---

    /// <summary>A fresh provider has created and disposed nothing.</summary>
    [Test]
    public async Task Counters_FreshProvider_AreZero(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new FakeTimeProvider());

        await Assert.That(time.TimersCreatedCount).IsEqualTo(0);
        await Assert.That(time.TimersDisposedCount).IsEqualTo(0);
    }

    /// <summary>The counters only grow: a disposal does not lower the created count, and disposing a timer
    /// twice (synchronously or asynchronously) counts once.</summary>
    [Test]
    public async Task Counters_CountEachCreationAndDisposalOnce(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new FakeTimeProvider());
        var first = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        var second = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        first.Dispose();
        first.Dispose();
        await second.DisposeAsync();
        await second.DisposeAsync();
        second.Dispose();

        await Assert.That(time.TimersCreatedCount).IsEqualTo(3);
        await Assert.That(time.TimersDisposedCount).IsEqualTo(2);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
    }

    // --- WaitUntilAsync ---

    /// <summary>A null condition is rejected.</summary>
    [Test]
    public async Task WaitUntilAsync_NullCondition_Throws(CancellationToken cancellationToken)
    {
        var time = new ObservableTimeProvider(new FakeTimeProvider());

        await Assert.That(async () => await time.WaitUntilAsync(null!, cancellationToken))
            .Throws<ArgumentNullException>();
    }

    /// <summary>A condition that already holds completes the wait at once, without any change.</summary>
    [Test]
    public async Task WaitUntilAsync_ConditionAlreadyHolds_CompletesAtOnce(CancellationToken cancellationToken)
    {
        var time = new ObservableTimeProvider(new FakeTimeProvider());

        var wait = time.WaitUntilAsync(static _ => true, cancellationToken);

        await Assert.That(wait.IsCompletedSuccessfully).IsTrue();
        await wait;
    }

    /// <summary>A canceled token ends the wait before the condition is checked.</summary>
    [Test]
    public async Task WaitUntilAsync_PreCanceledToken_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new FakeTimeProvider());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => await time.WaitUntilAsync(static _ => true, cts.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>Canceling the token while the wait is pending throws <see cref="OperationCanceledException"/>.</summary>
    [Test]
    public async Task WaitUntilAsync_CanceledWhileWaiting_Throws(CancellationToken cancellationToken)
    {
        var time = new ObservableTimeProvider(new FakeTimeProvider());
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                await cts.CancelAsync();
            },
            cancellationToken);

        await Assert.That(async () => await time.WaitUntilAsync(static _ => false, cts.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>The wait completes on the creation that makes the condition hold, not before it.</summary>
    [Test]
    public async Task WaitForTimersCreatedAsync_CreatedAfterWaitStarts_CompletesOnCreation(CancellationToken cancellationToken)
    {
        var time = new ObservableTimeProvider(new FakeTimeProvider());

        var wait = time.WaitForTimersCreatedAsync(2, cancellationToken);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        await Assert.That(wait.IsCompleted).IsFalse();

        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        await wait;

        await Assert.That(time.TimersCreatedCount).IsEqualTo(2);
    }

    /// <summary>A timer created before the wait starts satisfies it at once.</summary>
    [Test]
    public async Task WaitForTimersCreatedAsync_CreatedBeforeWaitStarts_CompletesAtOnce(CancellationToken cancellationToken)
    {
        var time = new ObservableTimeProvider(new FakeTimeProvider());
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        var wait = time.WaitForTimersCreatedAsync(1, cancellationToken);

        await Assert.That(wait.IsCompletedSuccessfully).IsTrue();
        await wait;
    }

    /// <summary>Timers created on another thread while the wait runs are seen, whichever side runs first.</summary>
    [Test]
    public async Task WaitForTimersCreatedAsync_CreatedOnAnotherThread_Completes(CancellationToken cancellationToken)
    {
        var time = new ObservableTimeProvider(new FakeTimeProvider());

        _ = Task.Run(
            async () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    await Task.Yield();
                    _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
                }
            },
            cancellationToken);

        await time.WaitForTimersCreatedAsync(20, cancellationToken);

        await Assert.That(time.TimersCreatedCount).IsEqualTo(20);
    }

    /// <summary>A disposal wakes the disposal wait.</summary>
    [Test]
    public async Task WaitForTimersDisposedAsync_DisposedAfterWaitStarts_CompletesOnDisposal(CancellationToken cancellationToken)
    {
        var time = new ObservableTimeProvider(new FakeTimeProvider());
        var timer = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        var wait = time.WaitForTimersDisposedAsync(1, cancellationToken);
        await Assert.That(wait.IsCompleted).IsFalse();

        await timer.DisposeAsync();
        await wait;

        await Assert.That(time.TimersDisposedCount).IsEqualTo(1);
    }

    /// <summary>A fire wakes the wait, so a condition on <see cref="ObservableTimeProvider.TimerFireCount"/>
    /// completes when the fake clock is advanced.</summary>
    [Test]
    public async Task WaitUntilAsync_TimerFires_CompletesOnFire(CancellationToken cancellationToken)
    {
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        var wait = time.WaitUntilAsync(static provider => provider.TimerFireCount >= 1, cancellationToken);
        await Assert.That(wait.IsCompleted).IsFalse();

        fake.Advance(OneSecond);
        await wait;
    }

    /// <summary>A schedule change wakes the wait, and once the new schedule is visible the inner timer carries
    /// it: advancing the fake clock right after the wait fires the timer.</summary>
    [Test]
    public async Task WaitUntilAsync_TimerChanged_InnerTimerCarriesNewSchedule(CancellationToken cancellationToken)
    {
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        var timer = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                timer.Change(FiveSeconds, Timeout.InfiniteTimeSpan);
            },
            cancellationToken);

        await time.WaitUntilAsync(static provider => provider.NextTimerDueTime == FiveSeconds, cancellationToken);
        fake.Advance(FiveSeconds);

        await Assert.That(time.TimerFireCount).IsEqualTo(1);
    }

    // --- visibility order, probed from inside the inner provider ---

    /// <summary>A creation is counted and tracked only once the inner timer exists, so a waiter woken by it can
    /// advance the inner clock and the timer fires.</summary>
    [Test]
    public async Task CreateTimer_CountsOnlyAfterInnerTimerExists(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new ProbeTimeProvider();
        var time = new ObservableTimeProvider(probe);
        long createdSeenByInner = -1;
        var activeSeenByInner = -1;
        probe.OnCreate = () =>
        {
            createdSeenByInner = time.TimersCreatedCount;
            activeSeenByInner = time.ActiveTimerCount;
        };

        _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);

        await Assert.That(createdSeenByInner).IsEqualTo(0);
        await Assert.That(activeSeenByInner).IsEqualTo(0);
        await Assert.That(time.TimersCreatedCount).IsEqualTo(1);
    }

    /// <summary>A new schedule is published only once the inner timer applied it: while the inner change runs,
    /// the provider still reports the previous schedule.</summary>
    [Test]
    public async Task Change_PublishesScheduleOnlyAfterInnerTimerAppliedIt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new ProbeTimeProvider();
        var time = new ObservableTimeProvider(probe);
        var timer = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        TimeSpan? dueSeenByInner = null;
        probe.OnChange = (_, _) => dueSeenByInner = time.NextTimerDueTime;

        var result = timer.Change(FiveSeconds, Timeout.InfiniteTimeSpan);

        await Assert.That(result).IsTrue();
        await Assert.That(dueSeenByInner).IsEqualTo(OneSecond);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(FiveSeconds);
    }

    /// <summary>A change the inner timer rejects leaves the previous schedule in place.</summary>
    [Test]
    public async Task Change_RejectedByInnerTimer_KeepsPreviousSchedule(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new ProbeTimeProvider();
        var time = new ObservableTimeProvider(probe);
        var timer = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        probe.OnChange = static (_, _) => throw new InvalidOperationException("The inner timer rejected the change.");

        await Assert.That(() => timer.Change(FiveSeconds, Timeout.InfiniteTimeSpan)).Throws<InvalidOperationException>();

        await Assert.That(time.NextTimerDueTime).IsEqualTo(OneSecond);
        probe.OnChange = null;
        timer.Change(FiveSeconds, Timeout.InfiniteTimeSpan);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(FiveSeconds);
    }

    /// <summary>A one-shot timer that fires during its own change (an inner provider may fire a timer that is
    /// due at once) is published as fired: disabled, with the fire counted.</summary>
    [Test]
    public async Task Change_OneShotFiresDuringChange_PublishedAsFired(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new ProbeTimeProvider();
        var time = new ObservableTimeProvider(probe);
        var timer = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, FiveSeconds);
        probe.OnChange = static (callback, state) => callback(state);

        timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        await Assert.That(time.TimerFireCount).IsEqualTo(1);
        await Assert.That(time.NextTimerDueTime).IsNull();
        await Assert.That(time.ActiveTimers[0].TimesFired).IsEqualTo(1);
    }

    /// <summary>A periodic timer that fires during its own change is published at its next period.</summary>
    [Test]
    public async Task Change_PeriodicFiresDuringChange_PublishedAtNextPeriod(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new ProbeTimeProvider();
        var time = new ObservableTimeProvider(probe);
        var timer = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        probe.OnChange = static (callback, state) => callback(state);

        timer.Change(TimeSpan.Zero, FiveSeconds);

        await Assert.That(time.TimerFireCount).IsEqualTo(1);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(FiveSeconds);
    }

    /// <summary>A disposal untracks and counts the timer before the inner timer is disposed (both
    /// <see cref="ITimer.Dispose"/> and <see cref="IAsyncDisposable.DisposeAsync"/>), so an inner provider that
    /// signals its own disposal never sees the timer as still active.</summary>
    [Test]
    public async Task Dispose_UntracksBeforeInnerTimerIsDisposed(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = new ProbeTimeProvider();
        var time = new ObservableTimeProvider(probe);
        var first = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        var second = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
        var activeSeenByInner = -1;
        long disposedSeenByInner = -1;
        probe.OnDispose = () =>
        {
            activeSeenByInner = time.ActiveTimerCount;
            disposedSeenByInner = time.TimersDisposedCount;
        };

        first.Dispose();
        await Assert.That(activeSeenByInner).IsEqualTo(1);
        await Assert.That(disposedSeenByInner).IsEqualTo(1);

        await second.DisposeAsync();
        await Assert.That(activeSeenByInner).IsEqualTo(0);
        await Assert.That(disposedSeenByInner).IsEqualTo(2);
    }

    /// <summary>
    /// An inner provider that lets a test run code at the moment the <see cref="ObservableTimeProvider"/> calls
    /// into it, to observe what the outer provider exposes at that point.
    /// </summary>
    private sealed class ProbeTimeProvider : TimeProvider
    {
        /// <summary>Gets or sets the code run when a timer is created.</summary>
        public Action? OnCreate { get; set; }

        /// <summary>Gets or sets the code run when a timer is changed; given the timer's callback and state, so
        /// it can fire the timer during the change.</summary>
        public Action<TimerCallback, object?>? OnChange { get; set; }

        /// <summary>Gets or sets the code run when a timer is disposed.</summary>
        public Action? OnDispose { get; set; }

        /// <inheritdoc/>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            OnCreate?.Invoke();
            return new ProbeTimer(this, callback, state);
        }

        /// <summary>A timer that only reports its calls to the owning probe.</summary>
        private sealed class ProbeTimer(ProbeTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            /// <inheritdoc/>
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.OnChange?.Invoke(callback, state);
                return true;
            }

            /// <inheritdoc/>
            public void Dispose() => owner.OnDispose?.Invoke();

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                owner.OnDispose?.Invoke();
                return ValueTask.CompletedTask;
            }
        }
    }
}
