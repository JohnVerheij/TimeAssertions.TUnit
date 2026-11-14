using System;

namespace TimeAssertions.TUnit;

/// <summary>
/// Distances between durations that do not overflow at the extremes of <see cref="TimeSpan"/>, where
/// <c>(a - b).Duration()</c> throws an <see cref="OverflowException"/>.
/// </summary>
internal static class TimeSpanDistance
{
    /// <summary>Gets the absolute difference of two durations.</summary>
    /// <param name="a">The first duration.</param>
    /// <param name="b">The second duration.</param>
    /// <returns>The absolute difference, or <see cref="TimeSpan.MaxValue"/> when it is larger than that.</returns>
    public static TimeSpan Between(TimeSpan a, TimeSpan b)
    {
        var difference = Int128.Abs((Int128)a.Ticks - b.Ticks);
        return difference > long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)difference);
    }

    /// <summary>Tells whether two durations are within a tolerance of each other, exactly, also when their
    /// difference is larger than <see cref="TimeSpan.MaxValue"/>.</summary>
    /// <param name="a">The first duration.</param>
    /// <param name="b">The second duration.</param>
    /// <param name="tolerance">The allowed absolute difference; not negative.</param>
    /// <returns><see langword="true"/> when the absolute difference is at most <paramref name="tolerance"/>.</returns>
    public static bool IsWithin(TimeSpan a, TimeSpan b, TimeSpan tolerance) =>
        Int128.Abs((Int128)a.Ticks - b.Ticks) <= tolerance.Ticks;
}
