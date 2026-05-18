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
}
