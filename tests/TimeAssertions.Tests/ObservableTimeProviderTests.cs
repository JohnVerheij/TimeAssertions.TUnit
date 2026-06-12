using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>Tests for the framework-agnostic <see cref="ObservableTimeProvider"/>: timer tracking
/// (creation, disposal, schedule capture, schedule change) and verbatim delegation of every
/// non-timer member to the wrapped inner provider. Uses a self-contained stub inner provider so the
/// core test project stays BCL-only (no <c>FakeTimeProvider</c> dependency).</summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class ObservableTimeProviderTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Constructor_NullInner_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => new ObservableTimeProvider(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task FreshProvider_HasZeroActiveTimers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
        await Assert.That(time.ActiveTimers.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CreateTimer_TracksScheduleAndCount(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        var info = time.ActiveTimers[0];
        await Assert.That(info.DueTime).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(info.Period).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DisposeTimer_RemovesFromActiveSet(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);

        timer.Dispose();
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
    }

    [Test]
    public async Task DisposeAsyncTimer_RemovesFromActiveSet(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        await timer.DisposeAsync();
        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
    }

    [Test]
    public async Task DoubleDisposeAsync_IsIdempotent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        var first = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        var second = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        await first.DisposeAsync();
        await first.DisposeAsync(); // already disposed: must hit the early-return, not remove the survivor

        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        second.Dispose();
    }

    [Test]
    public async Task DoubleDispose_IsIdempotent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        var first = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        var second = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        first.Dispose();
        first.Dispose(); // second dispose must not remove the surviving timer

        await Assert.That(time.ActiveTimerCount).IsEqualTo(1);
        second.Dispose();
    }

    [Test]
    public async Task ChangeTimer_UpdatesTrackedSchedule(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        var changed = timer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));

        await Assert.That(changed).IsTrue();
        var info = time.ActiveTimers[0];
        await Assert.That(info.DueTime).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(info.Period).IsEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task NextTimerDueTime_FreshProvider_IsNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        await Assert.That(time.NextTimerDueTime).IsNull();
    }

    [Test]
    public async Task NextTimerDueTime_SingleTimer_ReturnsItsDueTime(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        await Assert.That(time.NextTimerDueTime).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task NextTimerDueTime_MultipleTimers_ReturnsSmallestDueTime(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        await Assert.That(time.NextTimerDueTime).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task NextTimerDueTime_ExcludesDisabledTimers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        _ = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);

        await Assert.That(time.NextTimerDueTime).IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task NextTimerDueTime_OnlyDisabledTimers_IsNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        _ = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        await Assert.That(time.NextTimerDueTime).IsNull();
    }

    [Test]
    public async Task NextTimerDueTime_ReflectsScheduleChange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(TimeSpan.FromSeconds(5));

        _ = timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        await Assert.That(time.NextTimerDueTime).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task NextTimerDueTime_ChangedToDisabled_IsExcluded(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        _ = timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        await Assert.That(time.NextTimerDueTime).IsNull();
    }

    [Test]
    public async Task DelegatesClockMembersToInner(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inner = new StubTimeProvider(Epoch);
        var time = new ObservableTimeProvider(inner);

        await Assert.That(time.GetUtcNow()).IsEqualTo(Epoch);
        await Assert.That(time.GetTimestamp()).IsEqualTo(StubTimeProvider.FixedTimestamp);
        await Assert.That(time.TimestampFrequency).IsEqualTo(StubTimeProvider.FixedFrequency);
        await Assert.That(time.LocalTimeZone).IsEqualTo(TimeZoneInfo.Utc);

        inner.Set(Epoch + TimeSpan.FromHours(1));
        await Assert.That(time.GetUtcNow()).IsEqualTo(Epoch + TimeSpan.FromHours(1));
    }

    [Test]
    public async Task CreateTimer_NullCallback_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        await Assert.That(() => time.CreateTimer(null!, state: null, TimeSpan.Zero, Timeout.InfiniteTimeSpan))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task FreshProvider_TimerFireCountIsZero(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = new ObservableTimeProvider(new StubTimeProvider(Epoch));
        await Assert.That(time.TimerFireCount).IsEqualTo(0L);
    }

    [Test]
    public async Task Fire_CountsCumulativeAndPerTimer(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inner = new ControllableTimeProvider(Epoch);
        var time = new ObservableTimeProvider(inner);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        inner.FireAll();
        inner.FireAll();
        inner.FireAll();

        await Assert.That(time.TimerFireCount).IsEqualTo(3L);
        await Assert.That(time.ActiveTimers[0].TimesFired).IsEqualTo(3L);
    }

    [Test]
    public async Task Fire_InvokesConsumerCallbackWithItsState(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inner = new ControllableTimeProvider(Epoch);
        var time = new ObservableTimeProvider(inner);
        var marker = new object();
        object? observed = null;
        _ = time.CreateTimer(s => observed = s, marker, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        inner.FireAll();

        await Assert.That(observed).IsSameReferenceAs(marker);
        await Assert.That(time.TimerFireCount).IsEqualTo(1L);
    }

    [Test]
    public async Task Fire_PeriodicTimer_NextDueBecomesPeriod(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inner = new ControllableTimeProvider(Epoch);
        var time = new ObservableTimeProvider(inner);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        await Assert.That(time.NextTimerDueTime).IsEqualTo(TimeSpan.FromSeconds(5));

        inner.FireAll(); // after the first fire the next callback is one period away, not the creation due

        await Assert.That(time.NextTimerDueTime).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Fire_OneShotTimer_BecomesDisabled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inner = new ControllableTimeProvider(Epoch);
        var time = new ObservableTimeProvider(inner);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        inner.FireAll(); // a one-shot that has fired is disabled and excluded from the pending-due calculation

        await Assert.That(time.NextTimerDueTime).IsNull();
        await Assert.That(time.ActiveTimers[0].DueTime).IsEqualTo(Timeout.InfiniteTimeSpan);
    }

    [Test]
    public async Task Fire_CountSurvivesDisposal(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inner = new ControllableTimeProvider(Epoch);
        var time = new ObservableTimeProvider(inner);
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        inner.FireAll();
        timer.Dispose();

        await Assert.That(time.ActiveTimerCount).IsEqualTo(0);
        await Assert.That(time.TimerFireCount).IsEqualTo(1L); // cumulative count is not decremented on disposal
    }

    [Test]
    public async Task Fire_MultipleTimers_AccumulatesAcrossThem(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inner = new ControllableTimeProvider(Epoch);
        var time = new ObservableTimeProvider(inner);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        inner.FireAll(); // one fire each

        await Assert.That(time.TimerFireCount).IsEqualTo(2L);
    }

    /// <summary>A minimal deterministic <see cref="TimeProvider"/> for exercising
    /// <see cref="ObservableTimeProvider"/> without a real or fake timer implementation. Its timers
    /// are inert: they never fire, so tests observe only tracking and disposal behaviour.</summary>
    private sealed class StubTimeProvider : TimeProvider
    {
        public const long FixedTimestamp = 1_234_567L;
        public const long FixedFrequency = 1_000L;

        private DateTimeOffset _now;

        public StubTimeProvider(DateTimeOffset now) => _now = now;

        public void Set(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => FixedTimestamp;

        public override long TimestampFrequency => FixedFrequency;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new StubTimer();

        private sealed class StubTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
                // Inert: nothing to release.
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>A deterministic <see cref="TimeProvider"/> whose timers fire only when the test calls
    /// <see cref="FireAll"/>, so the BCL-only core test project can drive callback fires without a
    /// <c>FakeTimeProvider</c> dependency. Each created timer captures the callback the
    /// <see cref="ObservableTimeProvider"/> handed it (the fire-observing wrapper); firing it
    /// exercises the real fire-counting path.</summary>
    private sealed class ControllableTimeProvider : TimeProvider
    {
        private readonly List<ControllableTimer> _timers = [];
        private DateTimeOffset _now;

        public ControllableTimeProvider(DateTimeOffset now) => _now = now;

        /// <summary>Invokes every created, still-active timer's callback once.</summary>
        public void FireAll()
        {
            foreach (var timer in _timers)
            {
                timer.Fire();
            }
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => 0L;

        public override long TimestampFrequency => 1_000L;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControllableTimer(callback, state, _timers);
            _timers.Add(timer);
            return timer;
        }

        private sealed class ControllableTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private readonly List<ControllableTimer> _registry;

            public ControllableTimer(TimerCallback callback, object? state, List<ControllableTimer> registry)
            {
                _callback = callback;
                _state = state;
                _registry = registry;
            }

            public void Fire() => _callback(_state);

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => _registry.Remove(this);

            public ValueTask DisposeAsync()
            {
                _registry.Remove(this);
                return ValueTask.CompletedTask;
            }
        }
    }
}
