using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TimeAssertions;

/// <summary>
/// A <see cref="TimeProvider"/> decorator that tracks the <see cref="ITimer"/> instances created
/// against it so tests can assert on timer-disposal behaviour ("did this hosted service dispose
/// every timer it started?"). Every non-timer member delegates verbatim to the wrapped inner
/// provider, so it composes with any provider, most usefully a <c>FakeTimeProvider</c>: advance
/// fake time on the inner provider and assert on the active-timer set exposed here.
/// </summary>
/// <remarks>
/// <para>
/// This fills the gap that <c>FakeTimeProvider</c> leaves open. The BCL fake does not surface the
/// timers created against it (<see href="https://github.com/dotnet/extensions/issues/7515"/>), so
/// timer leaks in <c>BackgroundService</c> / <c>IHostedService</c> code cannot otherwise be
/// asserted without reflection. This wrapper observes
/// <see cref="CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)"/> and timer disposal
/// directly, staying allocation-light and reflection-free. If the upstream proposal lands, the
/// implementation can switch to the BCL API behind the same assertion surface with no consumer
/// change.
/// </para>
/// <para>
/// All members are thread-safe: production code under test typically creates and disposes timers
/// from background threads while the assertion reads the active set from the test thread.
/// </para>
/// </remarks>
public sealed class ObservableTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private readonly Lock _lock = new();
    private readonly HashSet<TrackedTimer> _activeTimers = [];

    /// <summary>
    /// Initializes a new <see cref="ObservableTimeProvider"/> that delegates every non-timer
    /// operation to <paramref name="inner"/> and intercepts timer creation for leak tracking.
    /// </summary>
    /// <param name="inner">The provider to wrap. Typically a <c>FakeTimeProvider</c> in tests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public ObservableTimeProvider(TimeProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Gets the number of timers created through this provider that have not yet been disposed.</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_lock)
            {
                return _activeTimers.Count;
            }
        }
    }

    /// <summary>
    /// Gets a point-in-time snapshot of the still-active timers, each described by the schedule it
    /// currently carries. The returned list is a copy taken under the internal lock; later creations
    /// or disposals do not mutate it. Order is unspecified; the failure-message renderer sorts
    /// deterministically.
    /// </summary>
    public IReadOnlyList<ActiveTimerInfo> ActiveTimers
    {
        get
        {
            lock (_lock)
            {
                var snapshot = new List<ActiveTimerInfo>(_activeTimers.Count);
                foreach (var timer in _activeTimers)
                {
                    snapshot.Add(timer.ToInfo());
                }

                return snapshot;
            }
        }
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    /// <inheritdoc/>
    public override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc/>
    public override long TimestampFrequency => _inner.TimestampFrequency;

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var innerTimer = _inner.CreateTimer(callback, state, dueTime, period);
        var tracked = new TrackedTimer(innerTimer, this, dueTime, period);
        lock (_lock)
        {
            _activeTimers.Add(tracked);
        }

        return tracked;
    }

    private void OnTimerDisposed(TrackedTimer timer)
    {
        lock (_lock)
        {
            _activeTimers.Remove(timer);
        }
    }

    /// <summary>
    /// Decorates an inner <see cref="ITimer"/> so disposal removes it from the owning provider's
    /// active set and schedule changes are reflected in the owner's snapshot.
    /// </summary>
    private sealed class TrackedTimer : ITimer
    {
        private readonly ITimer _inner;
        private readonly ObservableTimeProvider _owner;
        private TimeSpan _dueTime;
        private TimeSpan _period;
        private int _disposed;

        public TrackedTimer(ITimer inner, ObservableTimeProvider owner, TimeSpan dueTime, TimeSpan period)
        {
            _inner = inner;
            _owner = owner;
            _dueTime = dueTime;
            _period = period;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._lock)
            {
                _dueTime = dueTime;
                _period = period;
            }

            return _inner.Change(dueTime, period);
        }

        /// <summary>Projects the current schedule. Callers must hold the owner's lock.</summary>
        public ActiveTimerInfo ToInfo() => new(_dueTime, _period);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return;
            }

            _owner.OnTimerDisposed(this);
            _inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return ValueTask.CompletedTask;
            }

            _owner.OnTimerDisposed(this);
            return _inner.DisposeAsync();
        }
    }
}
