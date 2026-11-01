using System;
using System.Collections.Generic;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// Fluent timer-leak assertions on an <see cref="ObservableTimeProvider"/>. The headline use case
/// is verifying that hosted-service code (<c>BackgroundService</c> / <c>IHostedService</c> ping
/// loops, heartbeats, debounce timers) disposes every <see cref="System.Threading.ITimer"/> it
/// starts: wrap a <c>FakeTimeProvider</c> in an <see cref="ObservableTimeProvider"/>, run the code
/// under test, then assert the active-timer set.
/// </summary>
/// <remarks>
/// <para>
/// On failure these assertions name the surviving timers by the schedule they carry
/// (<c>[dueTime=…, period=…]</c>), so a leak is diagnosed by <em>which</em> timer remained rather
/// than by a bare integer count.
/// </para>
/// <para>
/// For an asynchronous disposal race (the timer is disposed on a background <c>StopAsync</c> that
/// has not completed when the assertion runs), use
/// <c>await Assert.That(time).HasNoActiveTimersEventually(cancellationToken)</c>, which re-checks the
/// live active count each time a timer changes until it reaches zero. The count-targeted siblings
/// (<c>HasActiveTimerCountEventually</c>, <c>HasAtLeastActiveTimerCountEventually</c>,
/// <c>HasAtMostActiveTimerCountEventually</c>, <c>HasActiveTimersEventually</c>) and
/// <c>HasTimerScheduledEventually</c> wait for other states the same way.
/// </para>
/// <para>
/// To verify how many times a timer's callback ran, advance fake time on the inner provider and
/// assert with <see cref="HasTimerFiredCount(ObservableTimeProvider, int)"/>,
/// <see cref="HasNoTimerFired(ObservableTimeProvider)"/>, or
/// <see cref="HasTimerFiredAtLeast(ObservableTimeProvider, int)"/>. The fire count is cumulative
/// across every timer the provider created and survives disposal.
/// </para>
/// </remarks>
public static class ActiveTimerAssertions
{
    /// <summary>
    /// Asserts that no timers created through the <see cref="ObservableTimeProvider"/> remain
    /// undisposed: the canonical timer-leak check after a hosted service has stopped.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when no timer is active; otherwise
    /// <see cref="AssertionResult.Failed(string)"/> with a message naming each surviving timer's
    /// schedule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasNoActiveTimers(this ObservableTimeProvider value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var active = value.ActiveTimers;
        return active.Count is 0
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatActiveTimerLeak(active));
    }

    /// <summary>
    /// Asserts that exactly <paramref name="expected"/> timers created through the
    /// <see cref="ObservableTimeProvider"/> are currently active. Useful for the registration half
    /// of a leak test ("the loop started its one timer") before the disposal half checks the count
    /// returns to zero.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <param name="expected">The expected active-timer count. Must be non-negative.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the active count equals
    /// <paramref name="expected"/>; otherwise <see cref="AssertionResult.Failed(string)"/> naming the
    /// active timers' schedules.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expected"/> is negative.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasActiveTimerCount(this ObservableTimeProvider value, int expected)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(expected);

        var active = value.ActiveTimers;
        return active.Count == expected
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatActiveTimerCountMismatch(active, expected));
    }

    /// <summary>
    /// Asserts that at least one timer created through the <see cref="ObservableTimeProvider"/> is
    /// currently active. The positive-presence counterpart of <see cref="HasNoActiveTimers"/>: useful
    /// for the registration half of a leak test ("the loop did start a timer") without pinning the
    /// exact count.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when at least one timer is active; otherwise
    /// <see cref="AssertionResult.Failed(string)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasActiveTimers(this ObservableTimeProvider value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.ActiveTimerCount >= 1
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatActiveTimerAtLeastShortfall(value.ActiveTimers, 1));
    }

    /// <summary>
    /// Asserts that at least <paramref name="count"/> timers created through the
    /// <see cref="ObservableTimeProvider"/> are currently active. Use it when a lower bound rather
    /// than an exact count is the natural expectation (for example "the pool started at least its
    /// minimum number of workers").
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <param name="count">The minimum expected active-timer count. Must be non-negative.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the active count is at least
    /// <paramref name="count"/>; otherwise <see cref="AssertionResult.Failed(string)"/> naming the
    /// active timers' schedules.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasAtLeastActiveTimerCount(this ObservableTimeProvider value, int count)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var active = value.ActiveTimers;
        return active.Count >= count
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatActiveTimerAtLeastShortfall(active, count));
    }

    /// <summary>
    /// Asserts that the next pending timer's due time is within <paramref name="tolerance"/> of
    /// <paramref name="expected"/>, inspecting the schedule the timer currently carries
    /// <em>without advancing the clock</em>. The "next" timer is the one with the smallest due time
    /// among the enabled (non-infinite) active timers. This verifies which delay a loop just
    /// scheduled (for example a step of an exponential backoff) rather than advancing fake time and
    /// inferring the delay from when the callback fires.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <param name="expected">The expected due time of the next pending timer.</param>
    /// <param name="tolerance">The allowed absolute tolerance around <paramref name="expected"/>.
    /// Must be non-negative.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when an enabled timer is pending and its due
    /// time is within <paramref name="tolerance"/> of <paramref name="expected"/>; otherwise
    /// <see cref="AssertionResult.Failed(string)"/> with a message naming the expected and observed
    /// due times, or noting that no enabled timer was pending.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasNextTimerDueApproximately(
        this ObservableTimeProvider value,
        TimeSpan expected,
        TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);

        var actual = value.NextTimerDueTime;
        if (actual is not null && (actual.Value - expected).Duration() <= tolerance)
        {
            return AssertionResult.Passed;
        }

        return AssertionResult.Failed(TimeRenderingHelpers.FormatNextTimerDueMismatch(actual, expected, tolerance));
    }

    /// <summary>
    /// Asserts that the next pending timer's due time falls within the inclusive range
    /// [<paramref name="min"/>, <paramref name="max"/>], inspecting the schedule the timer currently
    /// carries <em>without advancing the clock</em>. The "next" timer is the one with the smallest
    /// due time among the enabled (non-infinite) active timers.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <param name="min">The inclusive lower bound of the expected due-time range.</param>
    /// <param name="max">The inclusive upper bound of the expected due-time range. Must be greater
    /// than or equal to <paramref name="min"/>.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when an enabled timer is pending and its due
    /// time is within [<paramref name="min"/>, <paramref name="max"/>]; otherwise
    /// <see cref="AssertionResult.Failed(string)"/> with a message naming the range and observed due
    /// time, or noting that no enabled timer was pending.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is less than
    /// <paramref name="min"/>.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasPendingTimerDueWithin(
        this ObservableTimeProvider value,
        TimeSpan min,
        TimeSpan max)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);

        var actual = value.NextTimerDueTime;
        if (actual is not null && actual.Value >= min && actual.Value <= max)
        {
            return AssertionResult.Passed;
        }

        return AssertionResult.Failed(TimeRenderingHelpers.FormatNextTimerDueOutOfRange(actual, min, max));
    }

    /// <summary>
    /// Asserts that at least <paramref name="atLeast"/> active timers carry the due time
    /// <paramref name="dueTime"/>, inspecting the schedules the timers currently carry <em>without
    /// advancing the clock</em>. Unlike <see cref="HasNextTimerDueApproximately"/>, which looks at the
    /// earliest timer only, this finds a timer among any number of loops: "the auto-close is scheduled in
    /// 30 s" while a heartbeat runs every second, or "both ping loops armed their 30 s delay".
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <param name="dueTime">The due time to look for, as the code under test passed it (for example the
    /// delay of a <c>Task.Delay(delay, timeProvider, ct)</c>). Compared exactly. Must be non-negative.</param>
    /// <param name="atLeast">The minimum number of active timers carrying <paramref name="dueTime"/>. Must
    /// be positive.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when enough active timers carry
    /// <paramref name="dueTime"/>; otherwise <see cref="AssertionResult.Failed(string)"/> naming the active
    /// timers' schedules.</returns>
    /// <remarks>
    /// A timer carries the due time it was created or last changed with; after a fire, a periodic timer
    /// carries its period and a one-shot timer none. To wait for a timer that background code has yet to
    /// schedule, use <c>HasTimerScheduledEventually</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dueTime"/> is negative, or
    /// <paramref name="atLeast"/> is not positive.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasTimerScheduled(this ObservableTimeProvider value, TimeSpan dueTime, int atLeast = 1)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(atLeast);

        var active = value.ActiveTimers;
        return CountScheduledAt(active, dueTime) >= atLeast
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatScheduledTimerShortfall(active, dueTime, atLeast));
    }

    /// <summary>
    /// Asserts that no active timer has a pending due time: nothing is scheduled to fire. Unlike
    /// <see cref="HasNoActiveTimers"/>, an undisposed timer that is disabled (a one-shot timer that already
    /// fired, or one changed to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>) passes: it exists
    /// but will not fire.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when no active timer has a pending due time; otherwise
    /// <see cref="AssertionResult.Failed(string)"/> naming the scheduled timers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasNoTimerScheduled(this ObservableTimeProvider value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.NextTimerDueTime is null
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatUnexpectedScheduledTimers(value.ActiveTimers));
    }

    /// <summary>
    /// Asserts that timer callbacks fired exactly <paramref name="expected"/> times in total across
    /// every timer created through the <see cref="ObservableTimeProvider"/>. The count is cumulative
    /// and is not reset on disposal, so it reports how many times a hosted service's loop ran after a
    /// fake-time advance, without reading the loop's side effects.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <param name="expected">The expected cumulative fire count. Must be non-negative.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the cumulative fire count equals
    /// <paramref name="expected"/>; otherwise <see cref="AssertionResult.Failed(string)"/> with a
    /// message naming the expected and actual counts and the active timers' per-timer fire counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expected"/> is negative.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasTimerFiredCount(this ObservableTimeProvider value, int expected)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(expected);

        var actual = value.TimerFireCount;
        return actual == expected
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatTimerFireCountMismatch(actual, expected, value.ActiveTimers));
    }

    /// <summary>
    /// Asserts that no timer callback created through the <see cref="ObservableTimeProvider"/> has
    /// fired: the canonical check that a loop scheduled a timer but fake time was not advanced far
    /// enough to trigger it, or that a disposed timer never ran.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when no callback has fired; otherwise
    /// <see cref="AssertionResult.Failed(string)"/> with a message naming the cumulative fire count
    /// and the active timers' per-timer fire counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasNoTimerFired(this ObservableTimeProvider value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var actual = value.TimerFireCount;
        return actual is 0
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatNoTimerFired(actual, value.ActiveTimers));
    }

    /// <summary>
    /// Asserts that timer callbacks fired at least <paramref name="count"/> times in total across
    /// every timer created through the <see cref="ObservableTimeProvider"/>. The liveness lower-bound
    /// counterpart of <see cref="HasTimerFiredCount"/>: use it for a heartbeat where the exact number
    /// of fires is subject to timing jitter but a minimum cadence must be proven.
    /// </summary>
    /// <param name="value">The observable provider that tracked the timers.</param>
    /// <param name="count">The minimum cumulative fire count. Must be non-negative.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the cumulative fire count is at least
    /// <paramref name="count"/>; otherwise <see cref="AssertionResult.Failed(string)"/> with a
    /// message naming the minimum and actual counts and the active timers' per-timer fire counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult HasTimerFiredAtLeast(this ObservableTimeProvider value, int count)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var actual = value.TimerFireCount;
        return actual >= count
            ? AssertionResult.Passed
            : AssertionResult.Failed(TimeRenderingHelpers.FormatTimerFireShortfall(actual, count, value.ActiveTimers));
    }

    /// <summary>Counts the timers in <paramref name="timers"/> that carry <paramref name="dueTime"/>.</summary>
    /// <param name="timers">The timers to inspect.</param>
    /// <param name="dueTime">The due time to match exactly.</param>
    /// <returns>The number of matching timers.</returns>
    internal static int CountScheduledAt(IReadOnlyList<ActiveTimerInfo> timers, TimeSpan dueTime)
    {
        var count = 0;
        foreach (var timer in timers)
        {
            if (timer.DueTime == dueTime)
            {
                count++;
            }
        }

        return count;
    }
}
