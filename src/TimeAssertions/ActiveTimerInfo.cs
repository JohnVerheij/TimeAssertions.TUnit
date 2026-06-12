using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TimeAssertions;

/// <summary>
/// An immutable description of a timer tracked by <see cref="ObservableTimeProvider"/>: the
/// schedule it was created with, or last changed to. Used to render a deterministic
/// "named survivor" diagnostic when a leak assertion fails, so a failure names <em>which</em>
/// timer remained active by its schedule rather than reporting a bare count.
/// </summary>
/// <param name="DueTime">The delay before the timer's first (or next) callback, as supplied to
/// <see cref="TimeProvider.CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)"/>, the most
/// recent <see cref="ITimer.Change(TimeSpan, TimeSpan)"/>, or the timer's period once it has fired
/// (a periodic timer re-arms at its period; a non-periodic one, with a period of zero or
/// <see cref="Timeout.InfiniteTimeSpan"/>, is disabled after its fire). A value of
/// <see cref="Timeout.InfiniteTimeSpan"/> indicates a timer whose callback is currently disabled.</param>
/// <param name="Period">The interval between successive callbacks. A value of
/// <see cref="Timeout.InfiniteTimeSpan"/> indicates a one-shot timer that does not repeat.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ActiveTimerInfo(TimeSpan DueTime, TimeSpan Period)
{
    /// <summary>
    /// Gets the number of times this timer's callback has fired since it was created, as observed by
    /// <see cref="ObservableTimeProvider"/>. Zero for a timer that has not yet fired (the default
    /// when the value is constructed by name). With a <c>FakeTimeProvider</c> a fire is counted each
    /// time test code advances fake time past the timer's due (or period) boundary.
    /// </summary>
    public long TimesFired { get; init; }
}
