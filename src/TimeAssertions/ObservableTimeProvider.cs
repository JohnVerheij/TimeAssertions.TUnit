using System;
using System.Collections.Generic;
using System.Linq;
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
/// <see cref="CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)"/>, callback fires (via
/// <see cref="TimerFireCount"/>), and timer disposal directly, staying allocation-light and
/// reflection-free. If the upstream proposal lands, the implementation can switch to the BCL API
/// behind the same assertion surface with no consumer change.
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
    private long _timerFireCount;

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

    /// <summary>
    /// Gets the cumulative number of times a timer callback has fired across every timer created
    /// through this provider, counted from construction. The count is never decremented, so it
    /// survives a timer's disposal: a heartbeat loop that fired three times and then disposed its
    /// timer still reports <c>3</c>. With a <c>FakeTimeProvider</c> as the inner provider a fire
    /// happens only when test code advances fake time past a timer's due (or period) boundary, so
    /// this is a deterministic count of the callbacks the advances triggered, not a wall-clock race.
    /// </summary>
    public long TimerFireCount
    {
        get
        {
            lock (_lock)
            {
                return _timerFireCount;
            }
        }
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

    /// <summary>
    /// Gets the due time of the next pending timer: the smallest <see cref="ActiveTimerInfo.DueTime"/>
    /// among the still-active timers that are currently enabled, or <see langword="null"/> when no
    /// enabled timer is pending. A timer whose due time is <see cref="Timeout.InfiniteTimeSpan"/> is
    /// disabled (its callback will not fire until a <see cref="ITimer.Change(TimeSpan, TimeSpan)"/>
    /// re-arms it) and is excluded from the calculation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This reads the schedule the timer carries without advancing the clock, so a test can assert
    /// which delay a loop just scheduled (for example the next step of an exponential backoff)
    /// rather than advancing fake time and inferring the delay from when the callback fires. The due
    /// time is the value supplied at <see cref="TimeProvider.CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)"/>,
    /// the most recent <see cref="ITimer.Change(TimeSpan, TimeSpan)"/>, or the timer's period once it
    /// has fired (a periodic timer re-arms at its period; a one-shot becomes disabled); it is
    /// relative to the moment that schedule was set, not a remaining countdown.
    /// </para>
    /// <para>
    /// The value is computed under the internal lock, so it is a consistent snapshot even while
    /// background code creates, changes, or disposes timers concurrently.
    /// </para>
    /// </remarks>
    public TimeSpan? NextTimerDueTime
    {
        get
        {
            lock (_lock)
            {
                TimeSpan? earliest = null;
                foreach (var due in _activeTimers
                    .Select(static timer => timer.DueTime)
                    .Where(static due => due != Timeout.InfiniteTimeSpan))
                {
                    if (earliest is null || due < earliest.Value)
                    {
                        earliest = due;
                    }
                }

                return earliest;
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
        ArgumentNullException.ThrowIfNull(callback);

        // TrackedTimer wraps the callback so every fire is observed before the consumer's callback
        // runs; it creates its inner timer in its own constructor, keeping the inner reference
        // readonly (no two-phase init).
        var tracked = new TrackedTimer(this, callback, state, dueTime, period);
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
        private readonly ObservableTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly ITimer _inner;
        private TimeSpan _dueTime;
        private TimeSpan _period;
        private long _timesFired;
        private int _disposed;

        public TrackedTimer(
            ObservableTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _dueTime = dueTime;
            _period = period;

            // Create the inner timer last, once every field the fire path reads is set. The inner
            // provider does not fire synchronously during creation (a FakeTimeProvider fires only on
            // Advance), so OnInnerFired cannot run before the constructor completes.
            _inner = owner._inner.CreateTimer(OnInnerFired, state, dueTime, period);
        }

        /// <summary>
        /// The wrapper handed to the inner provider: observes the fire, then invokes the consumer's
        /// original callback with its original state.
        /// </summary>
        private void OnInnerFired(object? state)
        {
            NotifyFired();
            _callback(state);
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

        /// <summary>
        /// Records one callback fire: bumps this timer's and the owner's cumulative fire counts, and
        /// re-points the current due time to reflect the post-fire schedule. A timer with a
        /// strictly-positive period re-arms at that period; a non-periodic timer fires once and is
        /// then disabled (<see cref="Timeout.InfiniteTimeSpan"/>). Per the
        /// <see cref="TimeProvider.CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)"/> contract,
        /// non-periodic means a period of either <see cref="Timeout.InfiniteTimeSpan"/> or
        /// <see cref="TimeSpan.Zero"/>. This keeps
        /// <see cref="ObservableTimeProvider.NextTimerDueTime"/> correct after a timer fires rather
        /// than reporting the stale creation-time due. Runs under the owner lock so it is a consistent
        /// snapshot for concurrent readers.
        /// </summary>
        private void NotifyFired()
        {
            lock (_owner._lock)
            {
                _timesFired++;
                _owner._timerFireCount++;
                _dueTime = _period > TimeSpan.Zero ? _period : Timeout.InfiniteTimeSpan;
            }
        }

        /// <summary>Gets the current due time. Callers must hold the owner's lock.</summary>
        public TimeSpan DueTime => _dueTime;

        /// <summary>Projects the current schedule. Callers must hold the owner's lock.</summary>
        public ActiveTimerInfo ToInfo() => new(_dueTime, _period) { TimesFired = _timesFired };

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
