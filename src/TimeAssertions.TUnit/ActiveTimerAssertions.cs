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
}
