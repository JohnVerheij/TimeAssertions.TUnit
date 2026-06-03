using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

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

    /// <summary>
    /// Formats the failure message for <c>HasNoActiveTimers()</c>: names each timer that remained
    /// active by the schedule it was created with (or last changed to), so a leak failure reports
    /// <em>which</em> timers survived rather than a bare count. Survivors are listed in a
    /// deterministic order (by due time, then period) so the message is snapshot-stable.
    /// </summary>
    /// <param name="survivors">The timers still active when the assertion ran. Expected non-empty:
    /// the assertion only renders a failure when at least one timer leaked.</param>
    /// <returns>A multi-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>,
    /// with a grep-friendly <c>(count=N)</c> trailer on the headline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="survivors"/> is
    /// <see langword="null"/>.</exception>
    public static string FormatActiveTimerLeak(IReadOnlyList<ActiveTimerInfo> survivors)
    {
        ArgumentNullException.ThrowIfNull(survivors);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"expected no active timers but {survivors.Count} remained (count={survivors.Count}):");
        AppendTimerSchedules(sb, survivors);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure message for <c>HasActiveTimerCount(expected)</c>: the expected and actual
    /// active-timer counts, followed by each active timer's schedule in deterministic order (by due
    /// time, then period). When no timers are active the schedule list is omitted.
    /// </summary>
    /// <param name="active">The timers active when the assertion ran.</param>
    /// <param name="expected">The expected active-timer count that was not met. Must be
    /// non-negative.</param>
    /// <returns>A multi-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>,
    /// with a grep-friendly <c>(expected=N, actual=M)</c> trailer on the headline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expected"/> is negative.</exception>
    public static string FormatActiveTimerCountMismatch(IReadOnlyList<ActiveTimerInfo> active, int expected)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentOutOfRangeException.ThrowIfNegative(expected);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"expected {expected} active timer(s) but found {active.Count} (expected={expected}, actual={active.Count}):");
        AppendTimerSchedules(sb, active);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure message for <c>HasNextTimerDueApproximately(expected, tolerance)</c>: the
    /// expected due time, the allowed tolerance, and either the observed next-timer due time and how
    /// far it fell outside tolerance, or a note that no enabled timer was pending.
    /// </summary>
    /// <remarks>
    /// The rendered shape pins a one-line headline followed by a grep-friendly fixed-unit
    /// parenthetical (<c>(expected=Xms, tolerance=Yms, actual=Zms, delta=Wms)</c>) so CI log scrapers
    /// can extract the numbers without parsing the prose, analogous to
    /// <see cref="FormatBudgetOverrun"/>. When no enabled timer is pending the parenthetical reads
    /// <c>(expected=Xms, tolerance=Yms, actual=none)</c>.
    /// </remarks>
    /// <param name="actual">The observed next-timer due time, or <see langword="null"/> when no
    /// enabled timer is pending.</param>
    /// <param name="expected">The expected next-timer due time.</param>
    /// <param name="tolerance">The allowed absolute tolerance around <paramref name="expected"/>.
    /// Must be non-negative.</param>
    /// <returns>A single-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    public static string FormatNextTimerDueMismatch(TimeSpan? actual, TimeSpan expected, TimeSpan tolerance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);

        var expectedMs = expected.TotalMilliseconds;
        var toleranceMs = tolerance.TotalMilliseconds;

        if (actual is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"expected the next timer due in approximately {FormatDuration(expected)} (±{FormatDuration(tolerance)}) but no enabled timer was pending (expected={expectedMs:F0}ms, tolerance={toleranceMs:F0}ms, actual=none)");
        }

        var actualValue = actual.Value;
        var delta = (actualValue - expected).Duration();
        var actualMs = actualValue.TotalMilliseconds;
        var deltaMs = delta.TotalMilliseconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"expected the next timer due in approximately {FormatDuration(expected)} (±{FormatDuration(tolerance)}) but it was due in {FormatDuration(actualValue)}, off by {FormatDuration(delta)} (expected={expectedMs:F0}ms, tolerance={toleranceMs:F0}ms, actual={actualMs:F0}ms, delta={deltaMs:F0}ms)");
    }

    /// <summary>
    /// Formats the failure message for <c>HasPendingTimerDueWithin(min, max)</c>: the inclusive
    /// range, and either the observed next-timer due time, or a note that no enabled timer was
    /// pending.
    /// </summary>
    /// <remarks>
    /// The rendered shape pins a one-line headline followed by a grep-friendly fixed-unit
    /// parenthetical (<c>(min=Xms, max=Yms, actual=Zms)</c>); when no enabled timer is pending the
    /// parenthetical reads <c>(min=Xms, max=Yms, actual=none)</c>.
    /// </remarks>
    /// <param name="actual">The observed next-timer due time, or <see langword="null"/> when no
    /// enabled timer is pending.</param>
    /// <param name="min">The inclusive lower bound of the expected due-time range.</param>
    /// <param name="max">The inclusive upper bound of the expected due-time range.</param>
    /// <returns>A single-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    public static string FormatNextTimerDueOutOfRange(TimeSpan? actual, TimeSpan min, TimeSpan max)
    {
        var minMs = min.TotalMilliseconds;
        var maxMs = max.TotalMilliseconds;

        if (actual is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"expected a pending timer due within [{FormatDuration(min)}, {FormatDuration(max)}] but no enabled timer was pending (min={minMs:F0}ms, max={maxMs:F0}ms, actual=none)");
        }

        var actualValue = actual.Value;
        var actualMs = actualValue.TotalMilliseconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"expected a pending timer due within [{FormatDuration(min)}, {FormatDuration(max)}] but it was due in {FormatDuration(actualValue)} (min={minMs:F0}ms, max={maxMs:F0}ms, actual={actualMs:F0}ms)");
    }

    /// <summary>
    /// Appends each timer's schedule on its own indented line, sorted by due time then period for
    /// deterministic (snapshot-stable) output. A <see cref="Timeout.InfiniteTimeSpan"/> period
    /// renders as <c>one-shot</c>; an infinite due time renders as <c>infinite</c>.
    /// </summary>
    /// <param name="sb">The target builder; the schedule lines are appended after its current content.</param>
    /// <param name="timers">The timers to render. May be empty, in which case nothing is appended.</param>
    private static void AppendTimerSchedules(StringBuilder sb, IReadOnlyList<ActiveTimerInfo> timers)
    {
        var ordered = new List<ActiveTimerInfo>(timers);
        ordered.Sort(static (a, b) =>
        {
            var byDue = a.DueTime.CompareTo(b.DueTime);
            return byDue is not 0 ? byDue : a.Period.CompareTo(b.Period);
        });

        foreach (var timer in ordered)
        {
            var due = timer.DueTime == Timeout.InfiniteTimeSpan ? "infinite" : FormatDuration(timer.DueTime);
            var period = timer.Period == Timeout.InfiniteTimeSpan ? "one-shot" : FormatDuration(timer.Period);
            sb.AppendLine().Append(CultureInfo.InvariantCulture, $"  [dueTime={due}, period={period}]");
        }
    }
}
