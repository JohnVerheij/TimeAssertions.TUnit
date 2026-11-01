using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TimeAssertions;

/// <summary>
/// A <see cref="TimeProvider"/> decorator that tracks the <see cref="ITimer"/> instances created
/// against it so tests can assert on timer-disposal behavior ("did this hosted service dispose
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
/// Every observed change (a timer created, re-scheduled, fired, or disposed) wakes the waits:
/// <see cref="WaitUntilAsync"/>, <see cref="WaitForTimersCreatedAsync"/>, and
/// <see cref="WaitForTimersDisposedAsync"/> re-check their condition at the moment the state changes,
/// with no polling and no real-time delay; the <see cref="CancellationToken"/> is the only bound.
/// </para>
/// <para>
/// All members are thread-safe: production code under test typically creates and disposes timers
/// from background threads while the assertion reads the active set from the test thread. No caller
/// code runs under the internal lock, and wait continuations never run on the thread that changed the
/// state.
/// </para>
/// </remarks>
public class ObservableTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private readonly Lock _lock = new();
    private readonly HashSet<TrackedTimer> _activeTimers = [];
    private long _timerFireCount;
    private long _oneShotFireCount;
    private long _timersCreatedCount;
    private long _timersDisposedCount;
    private long _timersArmedByChangeCount;

    /// <summary>The pending change signal; taken and completed on every observed change. Guarded by the lock.</summary>
    private TaskCompletionSource? _changed;

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

    /// <summary>
    /// Gets the cumulative number of timers created through this provider, counted from construction.
    /// Unlike <see cref="ActiveTimerCount"/> it never decreases, so it answers "has the loop armed its
    /// next timer yet" even when the previous one-shot timer already fired and was disposed. A timer is
    /// counted once the inner provider has scheduled it, so an advance from then on can fire it.
    /// </summary>
    public long TimersCreatedCount
    {
        get
        {
            lock (_lock)
            {
                return _timersCreatedCount;
            }
        }
    }

    /// <summary>
    /// Gets the cumulative number of timers created through this provider that have been disposed,
    /// counted from construction. A timer is counted on its first disposal only, at the moment it
    /// leaves the active set (before its inner timer is disposed).
    /// </summary>
    public long TimersDisposedCount
    {
        get
        {
            lock (_lock)
            {
                return _timersDisposedCount;
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

    /// <summary>
    /// Gets the cumulative number of fires of non-periodic (one-shot) timers. A fired one-shot timer is
    /// done, so a loop that keeps running arms its next wait by creating a new timer or by re-scheduling a timer
    /// with <see cref="ITimer.Change"/>; the settle barrier relies on that.
    /// </summary>
    internal long OneShotFireCount
    {
        get
        {
            lock (_lock)
            {
                return _oneShotFireCount;
            }
        }
    }

    /// <summary>
    /// Gets the cumulative number of times a timer was armed: every creation, plus every
    /// <see cref="ITimer.Change"/> to a finite due time (the pattern of a callback that re-arms its own one-shot
    /// timer). Read under one lock, so the two parts are consistent.
    /// </summary>
    internal long TimersArmedCount
    {
        get
        {
            lock (_lock)
            {
                return _timersCreatedCount + _timersArmedByChangeCount;
            }
        }
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds. The condition is checked immediately and then again
    /// after every observed change (a timer created, re-scheduled, fired, or disposed), never on a polling
    /// loop. It runs on the waiting side, never under the internal lock, so it may read any member of the
    /// provider.
    /// </summary>
    /// <param name="condition">The condition to wait for, given this provider.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>A task that completes once <paramref name="condition"/> holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before the
    /// condition held.</exception>
    public async Task WaitUntilAsync(Func<ObservableTimeProvider, bool> condition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Take the change signal before checking, so a change between the check and the await is
            // not lost: it completes the signal taken here, and the loop checks again.
            TaskCompletionSource changed;
            lock (_lock)
            {
                changed = _changed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (condition(this))
            {
                return;
            }

            await changed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until at least <paramref name="count"/> timers have been created through this provider (see
    /// <see cref="TimersCreatedCount"/>). A creation is signaled only once the inner provider has scheduled the
    /// timer, so an advance after the wait can never come too early for it: wait for the timer first, then
    /// advance. The wait confirms creation, not that the timer is still pending: a one-shot timer that fired
    /// during its creation (a zero due time), or one created disabled, does not fire on the advance; a periodic
    /// timer that fired during its creation fires again after its period. The count covers every timer created
    /// through the provider, so it identifies a loop's timer only when the loop is the only code creating timers
    /// on it.
    /// </summary>
    /// <param name="count">The cumulative number of created timers to wait for.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>A task that completes once <see cref="TimersCreatedCount"/> reaches <paramref name="count"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled first.</exception>
    public Task WaitForTimersCreatedAsync(long count, CancellationToken cancellationToken)
        => WaitUntilAsync(provider => provider.TimersCreatedCount >= count, cancellationToken);

    /// <summary>
    /// Waits until at least <paramref name="count"/> timers created through this provider have been disposed
    /// (see <see cref="TimersDisposedCount"/>).
    /// </summary>
    /// <param name="count">The cumulative number of disposed timers to wait for.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>A task that completes once <see cref="TimersDisposedCount"/> reaches <paramref name="count"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled first.</exception>
    public Task WaitForTimersDisposedAsync(long count, CancellationToken cancellationToken)
        => WaitUntilAsync(provider => provider.TimersDisposedCount >= count, cancellationToken);

    /// <inheritdoc/>
    public sealed override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    /// <inheritdoc/>
    public sealed override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc/>
    public sealed override long TimestampFrequency => _inner.TimestampFrequency;

    /// <inheritdoc/>
    public sealed override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    /// <inheritdoc/>
    public sealed override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        // TrackedTimer wraps the callback so every fire is observed before the consumer's callback
        // runs; it creates its inner timer in its own constructor, keeping the inner reference
        // readonly (no two-phase init). The creation is counted and signaled only after that, so a
        // waiter woken by it can advance the inner clock and the timer fires.
        var tracked = new TrackedTimer(this, callback, state, dueTime, period);
        TaskCompletionSource? changed;
        lock (_lock)
        {
            _activeTimers.Add(tracked);
            _timersCreatedCount++;
            changed = TakeChangeSignal();
        }

        changed?.TrySetResult();
        return tracked;
    }

    /// <summary>
    /// Takes the pending change signal so the caller can complete it after releasing the lock. Callers must
    /// hold the lock. The signal runs its continuations asynchronously, so completing it never runs a waiter
    /// on the thread that changed the state.
    /// </summary>
    private TaskCompletionSource? TakeChangeSignal()
    {
        var changed = _changed;
        _changed = null;
        return changed;
    }

    /// <summary>Marks a schedule change of <paramref name="timer"/> as started, before the inner timer applies it.</summary>
    /// <returns>The change's sequence number on <paramref name="timer"/>.</returns>
    private long OnTimerChangeStarting(TrackedTimer timer, TimeSpan period)
    {
        lock (_lock)
        {
            return timer.BeginChange(period);
        }
    }

    /// <summary>
    /// Publishes the schedule of <paramref name="timer"/> once the inner timer carries it (or abandons the change
    /// when the inner timer rejected it) and signals the change.
    /// </summary>
    private void OnTimerChangeEnded(TrackedTimer timer, long change, TimeSpan dueTime, TimeSpan period, bool applied)
    {
        TaskCompletionSource? changed;
        lock (_lock)
        {
            timer.EndChange(change, dueTime, period, applied);
            if (applied && dueTime != Timeout.InfiniteTimeSpan)
            {
                _timersArmedByChangeCount++;
            }

            changed = TakeChangeSignal();
        }

        changed?.TrySetResult();
    }

    /// <summary>Records one fire of <paramref name="timer"/> and signals it.</summary>
    private void OnTimerFired(TrackedTimer timer)
    {
        TaskCompletionSource? changed;
        lock (_lock)
        {
            _timerFireCount++;
            if (!timer.RecordFire())
            {
                _oneShotFireCount++;
            }

            changed = TakeChangeSignal();
        }

        changed?.TrySetResult();
    }

    /// <summary>
    /// Untracks <paramref name="timer"/> and signals it. Runs before the inner timer is disposed, so the
    /// active set and <see cref="TimersDisposedCount"/> reflect the disposal as soon as it starts.
    /// </summary>
    private void OnTimerDisposed(TrackedTimer timer)
    {
        TaskCompletionSource? changed;
        lock (_lock)
        {
            _activeTimers.Remove(timer);
            _timersDisposedCount++;
            changed = TakeChangeSignal();
        }

        changed?.TrySetResult();
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

        /// <summary>Changes started but not yet applied by the inner timer. Guarded by the owner's lock.</summary>
        private int _changesPending;

        /// <summary>The period of the most recently started change. Guarded by the owner's lock.</summary>
        private TimeSpan _pendingPeriod;

        /// <summary>The number of changes started, which numbers each change. Guarded by the owner's lock.</summary>
        private long _changesStarted;

        /// <summary>The number of the change whose schedule is published, or 0. Guarded by the owner's lock.</summary>
        private long _publishedChange;

        /// <summary>The number of the change started last when the timer fired during a pending change, or 0.
        /// Guarded by the owner's lock.</summary>
        private long _changeFiredDuring;

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

            // Create the inner timer last, once every field the fire path reads is set: an inner provider may
            // fire during creation (a FakeTimeProvider fires a timer that is due at once), so OnInnerFired can
            // run before the constructor completes.
            _inner = owner._inner.CreateTimer(OnInnerFired, state, dueTime, period);
        }

        /// <summary>
        /// The wrapper handed to the inner provider: observes the fire, then invokes the consumer's
        /// original callback with its original state.
        /// </summary>
        private void OnInnerFired(object? state)
        {
            _owner.OnTimerFired(this);
            _callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            // Publish the new schedule only once the inner timer carries it: a waiter that sees it may advance
            // the inner clock right away, and the timer must then fire. A fire during the change (an inner
            // provider may fire a timer that is due at once) is applied to the new schedule when it is published.
            var change = _owner.OnTimerChangeStarting(this, period);
            var applied = false;
            try
            {
                applied = _inner.Change(dueTime, period);
                return applied;
            }
            finally
            {
                _owner.OnTimerChangeEnded(this, change, dueTime, period, applied);
            }
        }

        /// <summary>Marks a change as started. Callers must hold the owner's lock.</summary>
        /// <param name="period">The period the change applies.</param>
        /// <returns>The change's number: one more than the number of changes started before it.</returns>
        public long BeginChange(TimeSpan period)
        {
            _changesPending++;
            _pendingPeriod = period;
            return ++_changesStarted;
        }

        /// <summary>
        /// Ends a change: publishes the schedule when the inner timer applied it, as already fired when the timer
        /// fired while this change was the one started last. Changes can nest: a callback that a change fires may
        /// change its own timer again, and the inner timer then carries the inner change, which starts later but
        /// ends first. So a change publishes only when no change started after it has published; an outer change
        /// that ends afterwards leaves the published schedule alone. Callers must hold the owner's lock.
        /// </summary>
        /// <param name="change">The change's number, from <see cref="BeginChange"/>.</param>
        /// <param name="dueTime">The due time the change applies.</param>
        /// <param name="period">The period the change applies.</param>
        /// <param name="applied">Whether the inner timer accepted the change.</param>
        public void EndChange(long change, TimeSpan dueTime, TimeSpan period, bool applied)
        {
            _changesPending--;
            if (applied && change > _publishedChange)
            {
                _publishedChange = change;
                _period = period;
                _dueTime = _changeFiredDuring == change ? AfterFire(period) : dueTime;
            }
        }

        /// <summary>
        /// Records one callback fire: bumps this timer's fire count and re-points the current due time to
        /// the post-fire schedule. A timer with a strictly-positive period re-arms at that period; a
        /// non-periodic timer fires once and is then disabled (<see cref="Timeout.InfiniteTimeSpan"/>). Per
        /// the <see cref="TimeProvider.CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)"/> contract,
        /// non-periodic means a period of either <see cref="Timeout.InfiniteTimeSpan"/> or
        /// <see cref="TimeSpan.Zero"/>. This keeps <see cref="ObservableTimeProvider.NextTimerDueTime"/>
        /// correct after a timer fires rather than reporting the stale creation-time due. A fire during a
        /// change belongs to the change started last, which publishes the post-fire schedule. Callers must hold
        /// the owner's lock.
        /// </summary>
        /// <returns><see langword="true"/> when the timer is periodic; <see langword="false"/> for a one-shot.</returns>
        public bool RecordFire()
        {
            _timesFired++;
            if (_changesPending > 0)
            {
                _changeFiredDuring = _changesStarted;
                return _pendingPeriod > TimeSpan.Zero;
            }

            _dueTime = AfterFire(_period);
            return _period > TimeSpan.Zero;
        }

        /// <summary>The due time after a fire: the period for a periodic timer, otherwise disabled.</summary>
        private static TimeSpan AfterFire(TimeSpan period) => period > TimeSpan.Zero ? period : Timeout.InfiniteTimeSpan;

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

            // Untrack first, then dispose the inner timer: the disposal is visible (and signaled) as soon as
            // it starts, and a signaling inner provider composed under this one cannot observe the timer as
            // still tracked after its own disposal signal.
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
