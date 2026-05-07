using System;
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
}
