using System;
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
/// has not completed when the assertion runs), poll with the upstream TUnit primitive rather than a
/// family-specific overload:
/// <c>await Assert.That(() =&gt; time.ActiveTimerCount).Eventually(c =&gt; c == 0, timeout)</c>.
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
}
