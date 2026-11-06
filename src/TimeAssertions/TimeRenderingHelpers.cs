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
/// <remarks>
/// The failure texts describe only what was found: the assertion states what was expected (TUnit prints it as
/// <c>Expected …</c>, then the text as <c>but …</c>). Each text ends with a grep-friendly trailer and, where timers
/// are involved, the timers' schedules, introduced by a colon only when at least one timer follows.
/// </remarks>
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
    /// <returns>A one-line human-readable summary.</returns>
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
    /// <c>WasInvokedAtMostOncePer(...)</c>: the first consecutive pair that is too close, by index, with the
    /// observed gap and the configured minimum interval, for example
    /// <c>the gap at index 2 was 5.0s (minimum 30.0s)</c>.
    /// </summary>
    /// <remarks>
    /// The headline is followed by the two timestamps of the pair in ISO 8601 round-trip form, and a
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
            $"the gap at index {violatingIndex} was {FormatDuration(gap)} (minimum {FormatDuration(interval)})").AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"  timestamps[{violatingIndex - 1}]: {prior:O}").AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"  timestamps[{violatingIndex}]: {current:O}").AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"  (gap={gapMs:F0}ms, minimum={intervalMs:F0}ms)");
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasNoActiveTimers()</c> and <c>HasNoActiveTimersEventually(...)</c>: how
    /// many timers remained active, with a grep-friendly <c>(count=N)</c> trailer, followed by each survivor's
    /// schedule in deterministic order (by due time, then period), so a leak names <em>which</em> timers survived
    /// rather than a bare count. For example <c>2 remained (count=2):</c> and two schedule lines.
    /// </summary>
    /// <param name="survivors">The timers still active when the assertion ran.</param>
    /// <returns>A multi-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="survivors"/> is
    /// <see langword="null"/>.</exception>
    public static string FormatActiveTimerLeak(IReadOnlyList<ActiveTimerInfo> survivors)
    {
        ArgumentNullException.ThrowIfNull(survivors);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{survivors.Count} remained (count={survivors.Count})");
        AppendTimerSchedules(sb, survivors);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasActiveTimerCount(expected)</c> and
    /// <c>HasActiveTimerCountEventually(...)</c>: the active-timer count found, with a grep-friendly
    /// <c>(expected=N, actual=M)</c> trailer, followed by each active timer's schedule in deterministic order (by
    /// due time, then period). For example <c>found 2 (expected=3, actual=2):</c> and two schedule lines.
    /// </summary>
    /// <param name="active">The timers active when the assertion ran.</param>
    /// <param name="expected">The expected active-timer count that was not met. Must be
    /// non-negative.</param>
    /// <returns>A multi-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expected"/> is negative.</exception>
    public static string FormatActiveTimerCountMismatch(IReadOnlyList<ActiveTimerInfo> active, int expected)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentOutOfRangeException.ThrowIfNegative(expected);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"found {active.Count} (expected={expected}, actual={active.Count})");
        AppendTimerSchedules(sb, active);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasActiveTimers()</c>, <c>HasAtLeastActiveTimerCount(count)</c> and
    /// <c>HasAtLeastActiveTimerCountEventually(...)</c>: the active-timer count found, with a grep-friendly
    /// <c>(minimum=N, actual=M)</c> trailer, followed by each active timer's schedule in deterministic order (by
    /// due time, then period). For example <c>found 2 (minimum=3, actual=2):</c> and two schedule lines.
    /// </summary>
    /// <param name="active">The timers active when the assertion ran.</param>
    /// <param name="minimum">The minimum active-timer count that was required. Must be
    /// non-negative.</param>
    /// <returns>A multi-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimum"/> is negative.</exception>
    public static string FormatActiveTimerAtLeastShortfall(IReadOnlyList<ActiveTimerInfo> active, int minimum)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"found {active.Count} (minimum={minimum}, actual={active.Count})");
        AppendTimerSchedules(sb, active);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasTimerScheduled(dueTime, atLeast)</c> and
    /// <c>HasTimerScheduledEventually(...)</c>: how many active timers carry <paramref name="dueTime"/>, with a
    /// grep-friendly <c>(minimum=N, actual=M)</c> trailer and the number of active timers, followed by each active
    /// timer's schedule in deterministic order (by due time, then period). For example
    /// <c>found 1 (minimum=2, actual=1) among 2 active timer(s):</c> and two schedule lines.
    /// </summary>
    /// <param name="active">The timers active when the assertion ran.</param>
    /// <param name="dueTime">The due time the timers were expected to carry.</param>
    /// <param name="minimum">The minimum number of timers carrying <paramref name="dueTime"/>. Must be
    /// non-negative.</param>
    /// <returns>A multi-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimum"/> is negative.</exception>
    public static string FormatScheduledTimerShortfall(IReadOnlyList<ActiveTimerInfo> active, TimeSpan dueTime, int minimum)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);

        var actual = 0;
        foreach (var timer in active)
        {
            if (timer.DueTime == dueTime)
            {
                actual++;
            }
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"found {actual} (minimum={minimum}, actual={actual}) among {active.Count} active timer(s)");
        AppendTimerSchedules(sb, active);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasNoTimerScheduled()</c>: how many active timers still carry a pending
    /// (non-infinite) due time, with a grep-friendly <c>(count=N)</c> trailer, followed by those timers' schedules
    /// in deterministic order (by due time, then period). Active timers that are disabled are left out: they are
    /// not scheduled. For example <c>2 active timer(s) had a pending due time (count=2):</c> and two schedule lines.
    /// </summary>
    /// <param name="active">The timers active when the assertion ran.</param>
    /// <returns>A multi-line human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is
    /// <see langword="null"/>.</exception>
    public static string FormatUnexpectedScheduledTimers(IReadOnlyList<ActiveTimerInfo> active)
    {
        ArgumentNullException.ThrowIfNull(active);

        var scheduled = new List<ActiveTimerInfo>(active.Count);
        foreach (var timer in active)
        {
            if (timer.DueTime != Timeout.InfiniteTimeSpan)
            {
                scheduled.Add(timer);
            }
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"{scheduled.Count} active timer(s) had a pending due time (count={scheduled.Count})");
        AppendTimerSchedules(sb, scheduled);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasNextTimerDueApproximately(expected, tolerance)</c>: either the observed
    /// next-timer due time and how far it fell outside tolerance, or a note that no enabled timer was pending.
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
                $"no enabled timer was pending (expected={expectedMs:F0}ms, tolerance={toleranceMs:F0}ms, actual=none)");
        }

        var actualValue = actual.Value;
        var delta = (actualValue - expected).Duration();
        var actualMs = actualValue.TotalMilliseconds;
        var deltaMs = delta.TotalMilliseconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"it was due in {FormatDuration(actualValue)}, off by {FormatDuration(delta)} (expected={expectedMs:F0}ms, tolerance={toleranceMs:F0}ms, actual={actualMs:F0}ms, delta={deltaMs:F0}ms)");
    }

    /// <summary>
    /// Formats the failure text for <c>HasPendingTimerDueWithin(min, max)</c>: either the observed next-timer due
    /// time, or a note that no enabled timer was pending.
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
                $"no enabled timer was pending (min={minMs:F0}ms, max={maxMs:F0}ms, actual=none)");
        }

        var actualValue = actual.Value;
        var actualMs = actualValue.TotalMilliseconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"it was due in {FormatDuration(actualValue)} (min={minMs:F0}ms, max={maxMs:F0}ms, actual={actualMs:F0}ms)");
    }

    /// <summary>
    /// Formats the schedules of <paramref name="survivors"/> for a failure text whose headline the caller supplies,
    /// for example the one of <c>HasAtMostActiveTimerCountEventually(...)</c>: a colon and one indented line per
    /// timer, sorted by due time then period for deterministic (snapshot-stable) output.
    /// </summary>
    /// <param name="survivors">The timers still active when the wait ended.</param>
    /// <returns>A colon followed by one indented schedule line per timer, in
    /// <see cref="CultureInfo.InvariantCulture"/>; empty when <paramref name="survivors"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="survivors"/> is
    /// <see langword="null"/>.</exception>
    public static string FormatActiveTimerSurvivors(IReadOnlyList<ActiveTimerInfo> survivors)
    {
        ArgumentNullException.ThrowIfNull(survivors);

        var sb = new StringBuilder();
        AppendTimerSchedules(sb, survivors);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasTimerFiredCount(expected)</c>: the cumulative fire count found, with a
    /// grep-friendly <c>(expected=N, actual=M)</c> trailer, followed by the still-active timers with their
    /// per-timer fire counts. The count is cumulative and survives disposal, so a mismatch with no active timer
    /// still reports the totals. For example <c>they fired 1 time(s) (expected=3, actual=1):</c> and the timers.
    /// </summary>
    /// <param name="actual">The observed cumulative fire count.</param>
    /// <param name="expected">The expected fire count that was not met. Must be non-negative.</param>
    /// <param name="active">The timers still active when the assertion ran.</param>
    /// <returns>A human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expected"/> is negative.</exception>
    public static string FormatTimerFireCountMismatch(long actual, int expected, IReadOnlyList<ActiveTimerInfo> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentOutOfRangeException.ThrowIfNegative(expected);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"they fired {actual} time(s) (expected={expected}, actual={actual})");
        AppendTimerFireCounts(sb, active);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasNoTimerFired()</c>: the cumulative fire count that should have been
    /// zero, with a grep-friendly <c>(expected=0, actual=M)</c> trailer, followed by the still-active timers with
    /// their per-timer fire counts. For example <c>1 fired (expected=0, actual=1):</c> and the timers.
    /// </summary>
    /// <param name="actual">The observed cumulative fire count. Expected greater than zero: the
    /// assertion only renders a failure when at least one callback fired.</param>
    /// <param name="active">The timers still active when the assertion ran.</param>
    /// <returns>A human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is <see langword="null"/>.</exception>
    public static string FormatNoTimerFired(long actual, IReadOnlyList<ActiveTimerInfo> active)
    {
        ArgumentNullException.ThrowIfNull(active);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{actual} fired (expected=0, actual={actual})");
        AppendTimerFireCounts(sb, active);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the failure text for <c>HasTimerFiredAtLeast(minimum)</c>: the cumulative fire count found, with a
    /// grep-friendly <c>(minimum=N, actual=M)</c> trailer, followed by the still-active timers with their
    /// per-timer fire counts. The liveness counterpart used when timing jitter makes an exact fire count brittle.
    /// For example <c>they fired 1 time(s) (minimum=5, actual=1):</c> and the timers.
    /// </summary>
    /// <param name="actual">The observed cumulative fire count.</param>
    /// <param name="minimum">The minimum fire count that was required. Must be non-negative.</param>
    /// <param name="active">The timers still active when the assertion ran.</param>
    /// <returns>A human-readable summary in <see cref="CultureInfo.InvariantCulture"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimum"/> is negative.</exception>
    public static string FormatTimerFireShortfall(long actual, int minimum, IReadOnlyList<ActiveTimerInfo> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"they fired {actual} time(s) (minimum={minimum}, actual={actual})");
        AppendTimerFireCounts(sb, active);
        return sb.ToString();
    }

    /// <summary>
    /// Appends a colon and each active timer's schedule and fire count on its own indented line, sorted by due
    /// time then period for deterministic (snapshot-stable) output. When no timers are active nothing is
    /// appended, so the cumulative-count headline stands alone.
    /// </summary>
    /// <param name="sb">The target builder; the lines are appended after its current content.</param>
    /// <param name="timers">The active timers to render. May be empty.</param>
    private static void AppendTimerFireCounts(StringBuilder sb, IReadOnlyList<ActiveTimerInfo> timers)
    {
        foreach (var timer in Ordered(sb, timers))
        {
            sb.AppendLine().Append(CultureInfo.InvariantCulture,
                $"  [dueTime={FormatDueTime(timer)}, period={FormatPeriod(timer)}, fired={timer.TimesFired}]");
        }
    }

    /// <summary>
    /// Appends a colon and each timer's schedule on its own indented line, sorted by due time then period for
    /// deterministic (snapshot-stable) output. When there is no timer nothing is appended.
    /// </summary>
    /// <param name="sb">The target builder; the schedule lines are appended after its current content.</param>
    /// <param name="timers">The timers to render. May be empty, in which case nothing is appended.</param>
    private static void AppendTimerSchedules(StringBuilder sb, IReadOnlyList<ActiveTimerInfo> timers)
    {
        foreach (var timer in Ordered(sb, timers))
        {
            sb.AppendLine().Append(CultureInfo.InvariantCulture,
                $"  [dueTime={FormatDueTime(timer)}, period={FormatPeriod(timer)}]");
        }
    }

    /// <summary>
    /// Sorts <paramref name="timers"/> by due time, then period, and appends the colon that introduces them to
    /// <paramref name="sb"/> when there is at least one.
    /// </summary>
    /// <param name="sb">The target builder.</param>
    /// <param name="timers">The timers to render.</param>
    /// <returns>The timers in rendering order.</returns>
    private static List<ActiveTimerInfo> Ordered(StringBuilder sb, IReadOnlyList<ActiveTimerInfo> timers)
    {
        var ordered = new List<ActiveTimerInfo>(timers);
        ordered.Sort(static (a, b) =>
        {
            var byDue = a.DueTime.CompareTo(b.DueTime);
            return byDue is not 0 ? byDue : a.Period.CompareTo(b.Period);
        });

        if (ordered.Count > 0)
        {
            sb.Append(':');
        }

        return ordered;
    }

    /// <summary>Renders a timer's due time; <see cref="Timeout.InfiniteTimeSpan"/> (disabled) as <c>infinite</c>.</summary>
    /// <param name="timer">The timer.</param>
    /// <returns>The due time text.</returns>
    private static string FormatDueTime(ActiveTimerInfo timer) =>
        timer.DueTime == Timeout.InfiniteTimeSpan ? "infinite" : FormatDuration(timer.DueTime);

    /// <summary>Renders a timer's period. A non-periodic timer (period <see cref="Timeout.InfiniteTimeSpan"/> or
    /// <see cref="TimeSpan.Zero"/>, per the <see cref="TimeProvider.CreateTimer"/> contract) renders as
    /// <c>one-shot</c>.</summary>
    /// <param name="timer">The timer.</param>
    /// <returns>The period text.</returns>
    private static string FormatPeriod(ActiveTimerInfo timer) =>
        timer.Period == Timeout.InfiniteTimeSpan || timer.Period == TimeSpan.Zero ? "one-shot" : FormatDuration(timer.Period);
}
