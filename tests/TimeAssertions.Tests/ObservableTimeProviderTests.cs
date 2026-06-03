using System;
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
}
