using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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

        var minutes = duration.Ticks / TimeSpan.TicksPerMinute;
        var remainingSeconds = duration.Seconds;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes}:{remainingSeconds:D2}");
    }

    /// <summary>
    /// Formats a budget-overrun summary: actual elapsed, the budget that was exceeded, and the
    /// excess. Used in <c>.WithinTimeBudget(...)</c> failure messages.
    /// </summary>
    /// <remarks>
    /// The rendered string carries two forms in parallel: a human-readable prose
    /// (<c>completed in 1.2s: exceeded budget of 500ms by 747ms</c>) and a grep-friendly
    /// fixed-unit parenthetical (<c>(elapsed=1247ms, budget=500ms, overrun=747ms)</c>). The
    /// parenthetical lets CI log scrapers and triage tooling extract the three numbers
    /// without parsing the human-readable prose around them.
    /// </remarks>
    /// <param name="elapsed">The wall-clock duration the assertion's evaluator took.</param>
    /// <param name="budget">The configured timing budget.</param>
    /// <returns>A multi-line human-readable summary.</returns>
    public static string FormatBudgetOverrun(TimeSpan elapsed, TimeSpan budget)
    {
        var excess = elapsed - budget;
        var elapsedMs = elapsed.TotalMilliseconds;
        var budgetMs = budget.TotalMilliseconds;
        var excessMs = excess.TotalMilliseconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"completed in {FormatDuration(elapsed)}: exceeded budget of {FormatDuration(budget)} by {FormatDuration(excess)} (elapsed={elapsedMs:F0}ms, budget={budgetMs:F0}ms, overrun={excessMs:F0}ms)");
    }

    /// <summary>
    /// Formats a rate-limit violation summary for the failure message of
    /// <c>WasInvokedAtMostOncePer(...)</c>. Names the first violating consecutive pair by
    /// index, the observed gap, and the configured minimum interval.
    /// </summary>
    /// <remarks>
    /// The rendered shape pins a one-line headline with the violation index and gap,
    /// followed by the two violating timestamps in ISO 8601 round-trip form, and a
    /// grep-friendly fixed-unit parenthetical (analogous to
    /// <see cref="FormatBudgetOverrun"/>'s <c>(elapsed=, budget=, overrun=)</c> trailer)
    /// so CI log scrapers can extract the numbers without parsing the prose.
    /// </remarks>
    /// <param name="timestamps">The invocation-timestamp sequence the assertion examined.</param>
    /// <param name="violatingIndex">The zero-based index of the second timestamp in the
    /// violating pair. The pair is (<c>timestamps[violatingIndex - 1]</c>,
    /// <c>timestamps[violatingIndex]</c>).</param>
    /// <param name="gap">The observed gap between the two timestamps.</param>
    /// <param name="interval">The configured minimum interval that was violated.</param>
    /// <returns>A multi-line human-readable summary in
    /// <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timestamps"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="violatingIndex"/>
    /// is less than <c>1</c> or greater than or equal to
    /// <c><paramref name="timestamps"/>.Count</c>: the pair
    /// (<c>timestamps[violatingIndex - 1]</c>, <c>timestamps[violatingIndex]</c>)
    /// must reference two in-range entries.</exception>
    public static string FormatRateLimitViolation(
        IReadOnlyList<DateTimeOffset> timestamps,
        int violatingIndex,
        TimeSpan gap,
        TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentOutOfRangeException.ThrowIfLessThan(violatingIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(violatingIndex, timestamps.Count);

        var prior = timestamps[violatingIndex - 1];
        var current = timestamps[violatingIndex];
        var gapMs = gap.TotalMilliseconds;
        var intervalMs = interval.TotalMilliseconds;

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"interval violation at index {violatingIndex}: gap was {FormatDuration(gap)} (minimum {FormatDuration(interval)})").AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"  timestamps[{violatingIndex - 1}]: {prior:O}").AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"  timestamps[{violatingIndex}]:   {current:O}").AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"  (gap={gapMs:F0}ms, minimum={intervalMs:F0}ms)");
        return sb.ToString();
    }
}
