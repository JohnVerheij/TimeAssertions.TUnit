using System;
using System.Globalization;

namespace TimeAssertions;

/// <summary>
/// Formatting utilities for rendering elapsed durations and timing budgets in human-readable
/// form for assertion-failure messages. Pure; no I/O; allocation-conscious.
/// </summary>
public static class TimeRenderingHelpers
{
    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a compact human-readable duration string. Picks the
    /// most appropriate unit based on magnitude:
    /// sub-millisecond → microseconds (e.g. <c>"123μs"</c>),
    /// sub-second → milliseconds (e.g. <c>"247ms"</c>),
    /// sub-minute → seconds with one decimal (e.g. <c>"1.2s"</c>),
    /// otherwise → minutes:seconds (e.g. <c>"1:30"</c>).
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>A compact human-readable duration string in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            return "-" + FormatDuration(duration.Negate());
        }

        if (duration.TotalMilliseconds < 1.0)
        {
            var microseconds = duration.TotalMicroseconds;
            return string.Create(CultureInfo.InvariantCulture, $"{microseconds:F0}μs");
        }

        if (duration.TotalSeconds < 1.0)
        {
            var milliseconds = duration.TotalMilliseconds;
            return string.Create(CultureInfo.InvariantCulture, $"{milliseconds:F0}ms");
        }

        if (duration.TotalMinutes < 1.0)
        {
            var seconds = duration.TotalSeconds;
            return string.Create(CultureInfo.InvariantCulture, $"{seconds:F1}s");
        }

        var minutes = (long)duration.TotalMinutes;
        var remainingSeconds = duration.Seconds;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes}:{remainingSeconds:D2}");
    }

    /// <summary>
    /// Formats a budget-overrun summary: actual elapsed, the budget that was exceeded, and the
    /// excess. Used in <c>.WithinTimeBudget(...)</c> failure messages.
    /// </summary>
    /// <param name="elapsed">The wall-clock duration the assertion's evaluator took.</param>
    /// <param name="budget">The configured timing budget.</param>
    /// <returns>A multi-line human-readable summary.</returns>
    public static string FormatBudgetOverrun(TimeSpan elapsed, TimeSpan budget)
    {
        var excess = elapsed - budget;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"completed in {FormatDuration(elapsed)}: exceeded budget of {FormatDuration(budget)} by {FormatDuration(excess)}");
    }
}
