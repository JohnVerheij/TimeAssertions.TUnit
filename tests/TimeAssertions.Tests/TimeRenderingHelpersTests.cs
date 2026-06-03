using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>Pins the format selected by <see cref="TimeRenderingHelpers.FormatDuration"/> across
/// the magnitude bands (microseconds, milliseconds, seconds, minutes:seconds) and the negative
/// case. Each band is exercised with a representative value and at least one boundary case.</summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class TimeRenderingHelpersTests
{
    [Test]
    public async Task FormatDuration_Microseconds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatDuration(TimeSpan.FromMicroseconds(123));
        await Assert.That(formatted).IsEqualTo("123μs");
    }

    [Test]
    public async Task FormatDuration_BoundarySubMillisecond_StillMicroseconds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatDuration(TimeSpan.FromMicroseconds(999));
        await Assert.That(formatted).IsEqualTo("999μs");
    }

    [Test]
    public async Task FormatDuration_Milliseconds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatDuration(TimeSpan.FromMilliseconds(247));
        await Assert.That(formatted).IsEqualTo("247ms");
    }

    [Test]
    public async Task FormatDuration_Seconds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatDuration(TimeSpan.FromMilliseconds(1247));
        await Assert.That(formatted).IsEqualTo("1.2s");
    }

    [Test]
    public async Task FormatDuration_MinutesSeconds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatDuration(TimeSpan.FromSeconds(90));
        await Assert.That(formatted).IsEqualTo("1:30");
    }

    [Test]
    public async Task FormatDuration_Negative(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatDuration(TimeSpan.FromMilliseconds(-247));
        await Assert.That(formatted).IsEqualTo("-247ms");
    }

    [Test]
    public async Task FormatDuration_Zero(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatDuration(TimeSpan.Zero);
        await Assert.That(formatted).IsEqualTo("0μs");
    }

    [Test]
    public async Task FormatBudgetOverrun_IncludesActualBudgetAndExcess(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatBudgetOverrun(
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(500));

        await Assert.That(formatted).Contains("750ms");
        await Assert.That(formatted).Contains("500ms");
        await Assert.That(formatted).Contains("250ms");
    }

    /// <summary>Pins the grep-friendly uniform-millisecond suffix appended in v0.3.0. The
    /// suffix carries three named components in a fixed order so CI log scrapers and triage
    /// tooling can extract the numbers without parsing the human-readable prose.</summary>
    [Test]
    public async Task FormatBudgetOverrun_EmitsUniformMillisecondSuffixWithThreeNamedComponents(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatBudgetOverrun(
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(500));

        // The suffix is the substring after the human-readable prose. Pin the exact shape.
        await Assert.That(formatted).Contains("(elapsed=750ms, budget=500ms, overrun=250ms)");
    }

    /// <summary>Pins invariant-culture rendering of the F0 numeric format inside the suffix.
    /// Under cultures whose digit / separator conventions diverge from invariant, a naive
    /// <c>$"{x:F0}"</c> would render differently. The implementation uses
    /// <see cref="string.Create(IFormatProvider, ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler)"/>
    /// with <see cref="CultureInfo.InvariantCulture"/> to guarantee a fixed text shape across
    /// consumer locales. The test forces an ambient <c>nl-NL</c> culture (comma as decimal
    /// separator, dot as group separator) for its scope so a regression to
    /// <c>CurrentCulture</c>-based formatting would be observable; under invariant culture
    /// alone the assertion would pass vacuously.</summary>
    [Test]
    public async Task FormatBudgetOverrun_RendersSubsecondBudgetWithInvariantCultureF0(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            var formatted = TimeRenderingHelpers.FormatBudgetOverrun(
                TimeSpan.FromMilliseconds(1200),
                TimeSpan.FromMilliseconds(500));

            await Assert.That(formatted).Contains("(elapsed=1200ms, budget=500ms, overrun=700ms)");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>Pins the arithmetic invariant: the rendered <c>overrun</c> equals
    /// <c>elapsed - budget</c>, rounded to whole milliseconds by the F0 format. Exercised
    /// across five representative (elapsed, budget) pairs spanning ms / s / min scales.</summary>
    [Test]
    [Arguments(10, 5, 5)]
    [Arguments(100, 50, 50)]
    [Arguments(1_000, 500, 500)]
    [Arguments(30_000, 10_000, 20_000)]
    [Arguments(3_600_000, 1_800_000, 1_800_000)]
    public async Task FormatBudgetOverrun_OverrunValueEqualsElapsedMinusBudget(
        int elapsedMs,
        int budgetMs,
        int expectedOverrunMs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatBudgetOverrun(
            TimeSpan.FromMilliseconds(elapsedMs),
            TimeSpan.FromMilliseconds(budgetMs));

        await Assert.That(formatted).Contains(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"(elapsed={elapsedMs}ms, budget={budgetMs}ms, overrun={expectedOverrunMs}ms)"));
    }

    // ---- FormatRateLimitViolation (v0.5.0) ----

    /// <summary>The headline line names the violating index, the observed gap, and the
    /// minimum interval. Pins the user-visible prose without the grep-friendly trailer.</summary>
    [Test]
    public async Task FormatRateLimitViolation_HeadlineNamesIndexAndGap(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timestamps = new[] { epoch, epoch + TimeSpan.FromSeconds(5) };

        var formatted = TimeRenderingHelpers.FormatRateLimitViolation(
            timestamps,
            violatingIndex: 1,
            gap: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromSeconds(30));

        await Assert.That(formatted).Contains("interval violation at index 1");
        await Assert.That(formatted).Contains("gap was 5.0s");
        await Assert.That(formatted).Contains("minimum 30");
    }

    /// <summary>The grep-friendly parenthetical carries `(gap=Xms, minimum=Yms)` in
    /// fixed milliseconds for CI log scrapers, analogous to <c>FormatBudgetOverrun</c>'s
    /// `(elapsed=, budget=, overrun=)` trailer.</summary>
    [Test]
    public async Task FormatRateLimitViolation_RendersGrepFriendlyParenthetical(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timestamps = new[] { epoch, epoch + TimeSpan.FromMilliseconds(5_000) };

        var formatted = TimeRenderingHelpers.FormatRateLimitViolation(
            timestamps,
            violatingIndex: 1,
            gap: TimeSpan.FromMilliseconds(5_000),
            interval: TimeSpan.FromMilliseconds(30_000));

        await Assert.That(formatted).Contains("(gap=5000ms, minimum=30000ms)");
    }

    /// <summary>Argument validation: <see langword="null"/> timestamps list rejected
    /// with <see cref="ArgumentNullException"/> rather than producing a confusing
    /// downstream NRE.</summary>
    [Test]
    public async Task FormatRateLimitViolation_NullTimestamps_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => TimeRenderingHelpers.FormatRateLimitViolation(
                timestamps: null!,
                violatingIndex: 1,
                gap: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromSeconds(30)))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Argument validation: a <c>violatingIndex</c> of <c>0</c> would name
    /// a pair whose prior element is at index <c>-1</c>. Rejected with
    /// <see cref="ArgumentOutOfRangeException"/> rather than producing a confusing
    /// downstream <see cref="IndexOutOfRangeException"/>.</summary>
    [Test]
    public async Task FormatRateLimitViolation_ViolatingIndexZero_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timestamps = new[] { epoch, epoch + TimeSpan.FromSeconds(5) };

        await Assert.That(() => TimeRenderingHelpers.FormatRateLimitViolation(
                timestamps,
                violatingIndex: 0,
                gap: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromSeconds(30)))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>Argument validation: a <c>violatingIndex</c> equal to or beyond
    /// <c>timestamps.Count</c> would index out of range. Rejected with
    /// <see cref="ArgumentOutOfRangeException"/> rather than producing a confusing
    /// downstream <see cref="IndexOutOfRangeException"/>.</summary>
    [Test]
    public async Task FormatRateLimitViolation_ViolatingIndexAtCount_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timestamps = new[] { epoch, epoch + TimeSpan.FromSeconds(5) };

        await Assert.That(() => TimeRenderingHelpers.FormatRateLimitViolation(
                timestamps,
                violatingIndex: 2,
                gap: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromSeconds(30)))
            .Throws<ArgumentOutOfRangeException>();
    }

    // ---- FormatActiveTimerLeak / FormatActiveTimerCountMismatch (v0.6.0) ----

    /// <summary>A single leaked timer is named by its schedule, with the grep-friendly count
    /// trailer on the headline.</summary>
    [Test]
    public async Task FormatActiveTimerLeak_NamesSurvivorScheduleAndCount(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var survivors = new[] { new ActiveTimerInfo(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)) };

        var formatted = TimeRenderingHelpers.FormatActiveTimerLeak(survivors);

        await Assert.That(formatted).Contains("expected no active timers but 1 remained");
        await Assert.That(formatted).Contains("(count=1)");
        await Assert.That(formatted).Contains("[dueTime=1.0s, period=5.0s]");
    }

    /// <summary>Survivors render in a deterministic order (by due time, then period) regardless of
    /// input order, so the message is snapshot-stable. Exercises both the due-time ordering and the
    /// period tie-break.</summary>
    [Test]
    public async Task FormatActiveTimerLeak_SortsSurvivorsDeterministically(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var survivors = new[]
        {
            new ActiveTimerInfo(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)),
            new ActiveTimerInfo(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)),
            new ActiveTimerInfo(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
        };

        var formatted = TimeRenderingHelpers.FormatActiveTimerLeak(survivors);

        var first = formatted.IndexOf("dueTime=1.0s, period=1.0s", StringComparison.Ordinal);
        var second = formatted.IndexOf("dueTime=1.0s, period=5.0s", StringComparison.Ordinal);
        var third = formatted.IndexOf("dueTime=2.0s, period=10.0s", StringComparison.Ordinal);

        await Assert.That(first).IsGreaterThanOrEqualTo(0);
        await Assert.That(first).IsLessThan(second);
        await Assert.That(second).IsLessThan(third);
    }

    /// <summary>A one-shot timer (infinite period) renders as <c>one-shot</c>, and an infinite due
    /// time as <c>infinite</c>, rather than as negative durations.</summary>
    [Test]
    public async Task FormatActiveTimerLeak_RendersInfiniteScheduleWords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var survivors = new[] { new ActiveTimerInfo(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan) };

        var formatted = TimeRenderingHelpers.FormatActiveTimerLeak(survivors);

        await Assert.That(formatted).Contains("[dueTime=infinite, period=one-shot]");
    }

    /// <summary>Argument validation: a <see langword="null"/> survivor list is rejected.</summary>
    [Test]
    public async Task FormatActiveTimerLeak_NullSurvivors_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => TimeRenderingHelpers.FormatActiveTimerLeak(null!))
            .Throws<ArgumentNullException>();
    }

    /// <summary>A count mismatch renders the expected and actual counts plus the active timers'
    /// schedules, with the grep-friendly trailer.</summary>
    [Test]
    public async Task FormatActiveTimerCountMismatch_RendersExpectedActualAndSchedules(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = new[] { new ActiveTimerInfo(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };

        var formatted = TimeRenderingHelpers.FormatActiveTimerCountMismatch(active, expected: 2);

        await Assert.That(formatted).Contains("expected 2 active timer(s) but found 1");
        await Assert.That(formatted).Contains("(expected=2, actual=1)");
        await Assert.That(formatted).Contains("period=5.0s");
    }

    /// <summary>When no timers are active, the schedule list is omitted (nothing to render): only
    /// the headline with the expected and actual counts appears.</summary>
    [Test]
    public async Task FormatActiveTimerCountMismatch_EmptyActive_OmitsScheduleList(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = Array.Empty<ActiveTimerInfo>();

        var formatted = TimeRenderingHelpers.FormatActiveTimerCountMismatch(active, expected: 1);

        await Assert.That(formatted).Contains("expected 1 active timer(s) but found 0");
        await Assert.That(formatted).Contains("(expected=1, actual=0)");
        await Assert.That(formatted).DoesNotContain("[dueTime=");
    }

    /// <summary>Argument validation: a <see langword="null"/> active list is rejected.</summary>
    [Test]
    public async Task FormatActiveTimerCountMismatch_NullActive_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => TimeRenderingHelpers.FormatActiveTimerCountMismatch(null!, expected: 1))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Argument validation: a negative expected count is rejected.</summary>
    [Test]
    public async Task FormatActiveTimerCountMismatch_NegativeExpected_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = Array.Empty<ActiveTimerInfo>();

        await Assert.That(() => TimeRenderingHelpers.FormatActiveTimerCountMismatch(active, expected: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    // ---- FormatActiveTimerSurvivors / FormatActiveTimerAtLeastShortfall (v0.7.0) ----

    /// <summary>The survivor list renders the grep-friendly count trailer followed by each survivor's
    /// schedule, sorted deterministically.</summary>
    [Test]
    public async Task FormatActiveTimerSurvivors_RendersCountTrailerAndSchedules(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var survivors = new[]
        {
            new ActiveTimerInfo(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)),
            new ActiveTimerInfo(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)),
        };

        var formatted = TimeRenderingHelpers.FormatActiveTimerSurvivors(survivors);

        await Assert.That(formatted).Contains("(count=2)");
        var first = formatted.IndexOf("dueTime=1.0s, period=5.0s", StringComparison.Ordinal);
        var second = formatted.IndexOf("dueTime=2.0s, period=10.0s", StringComparison.Ordinal);
        await Assert.That(first).IsGreaterThanOrEqualTo(0);
        await Assert.That(first).IsLessThan(second);
    }

    /// <summary>An empty survivor list renders the <c>(count=0)</c> trailer alone, with no schedule
    /// lines.</summary>
    [Test]
    public async Task FormatActiveTimerSurvivors_Empty_RendersCountTrailerOnly(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatActiveTimerSurvivors(Array.Empty<ActiveTimerInfo>());

        await Assert.That(formatted).Contains("(count=0)");
        await Assert.That(formatted).DoesNotContain("[dueTime=");
    }

    /// <summary>Argument validation: a <see langword="null"/> survivor list is rejected.</summary>
    [Test]
    public async Task FormatActiveTimerSurvivors_Null_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => TimeRenderingHelpers.FormatActiveTimerSurvivors(null!))
            .Throws<ArgumentNullException>();
    }

    /// <summary>An at-least shortfall renders the required minimum and the actual count plus the
    /// active timers' schedules, with the grep-friendly trailer.</summary>
    [Test]
    public async Task FormatActiveTimerAtLeastShortfall_RendersMinimumActualAndSchedules(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = new[] { new ActiveTimerInfo(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)) };

        var formatted = TimeRenderingHelpers.FormatActiveTimerAtLeastShortfall(active, minimum: 3);

        await Assert.That(formatted).Contains("expected at least 3 active timer(s) but found 1");
        await Assert.That(formatted).Contains("(minimum=3, actual=1)");
        await Assert.That(formatted).Contains("[dueTime=1.0s, period=5.0s]");
    }

    /// <summary>When no timers are active, the schedule list is omitted: only the headline appears.</summary>
    [Test]
    public async Task FormatActiveTimerAtLeastShortfall_EmptyActive_OmitsScheduleList(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatActiveTimerAtLeastShortfall(Array.Empty<ActiveTimerInfo>(), minimum: 1);

        await Assert.That(formatted).Contains("expected at least 1 active timer(s) but found 0");
        await Assert.That(formatted).Contains("(minimum=1, actual=0)");
        await Assert.That(formatted).DoesNotContain("[dueTime=");
    }

    /// <summary>Argument validation: a <see langword="null"/> active list is rejected.</summary>
    [Test]
    public async Task FormatActiveTimerAtLeastShortfall_NullActive_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => TimeRenderingHelpers.FormatActiveTimerAtLeastShortfall(null!, minimum: 1))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Argument validation: a negative minimum is rejected.</summary>
    [Test]
    public async Task FormatActiveTimerAtLeastShortfall_NegativeMinimum_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => TimeRenderingHelpers.FormatActiveTimerAtLeastShortfall(Array.Empty<ActiveTimerInfo>(), minimum: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    // ---- FormatNextTimerDueMismatch / FormatNextTimerDueOutOfRange (v0.6.0) ----

    /// <summary>An out-of-tolerance next-timer due time names the expected and observed due times,
    /// the delta, and the grep-friendly trailer.</summary>
    [Test]
    public async Task FormatNextTimerDueMismatch_NamesExpectedActualAndDelta(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatNextTimerDueMismatch(
            actual: TimeSpan.FromMilliseconds(2400),
            expected: TimeSpan.FromMilliseconds(2000),
            tolerance: TimeSpan.FromMilliseconds(100));

        await Assert.That(formatted).Contains("expected the next timer due in approximately 2.0s");
        await Assert.That(formatted).Contains("(expected=2000ms, tolerance=100ms, actual=2400ms, delta=400ms)");
    }

    /// <summary>When no enabled timer is pending the message reads <c>actual=none</c> rather than
    /// reporting a misleading numeric due time.</summary>
    [Test]
    public async Task FormatNextTimerDueMismatch_NoPendingTimer_RendersNone(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatNextTimerDueMismatch(
            actual: null,
            expected: TimeSpan.FromSeconds(2),
            tolerance: TimeSpan.FromMilliseconds(1));

        await Assert.That(formatted).Contains("no enabled timer was pending");
        await Assert.That(formatted).Contains("(expected=2000ms, tolerance=1ms, actual=none)");
    }

    /// <summary>Argument validation: a negative tolerance is rejected.</summary>
    [Test]
    public async Task FormatNextTimerDueMismatch_NegativeTolerance_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => TimeRenderingHelpers.FormatNextTimerDueMismatch(
                actual: TimeSpan.FromSeconds(2),
                expected: TimeSpan.FromSeconds(2),
                tolerance: TimeSpan.FromMilliseconds(-1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>An out-of-range next-timer due time names the inclusive range, the observed due
    /// time, and the grep-friendly trailer.</summary>
    [Test]
    public async Task FormatNextTimerDueOutOfRange_NamesRangeAndActual(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatNextTimerDueOutOfRange(
            actual: TimeSpan.FromMilliseconds(5000),
            min: TimeSpan.FromMilliseconds(1000),
            max: TimeSpan.FromMilliseconds(4000));

        await Assert.That(formatted).Contains("expected a pending timer due within [1.0s, 4.0s]");
        await Assert.That(formatted).Contains("(min=1000ms, max=4000ms, actual=5000ms)");
    }

    /// <summary>When no enabled timer is pending the range message reads <c>actual=none</c>.</summary>
    [Test]
    public async Task FormatNextTimerDueOutOfRange_NoPendingTimer_RendersNone(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = TimeRenderingHelpers.FormatNextTimerDueOutOfRange(
            actual: null,
            min: TimeSpan.FromMilliseconds(1000),
            max: TimeSpan.FromMilliseconds(4000));

        await Assert.That(formatted).Contains("no enabled timer was pending");
        await Assert.That(formatted).Contains("(min=1000ms, max=4000ms, actual=none)");
    }
}
