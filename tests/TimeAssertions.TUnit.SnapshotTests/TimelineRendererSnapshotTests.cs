using System;
using System.Threading;
using System.Threading.Tasks;
using SnapshotAssertions.TUnit;
using TimeAssertions.Render;

namespace TimeAssertions.TUnit.SnapshotTests;

/// <summary>
/// End-to-end integration test for the canonical "render a timeline, pin via snapshot"
/// pattern documented in the <c>README.md</c> cookbook. Exercises
/// <see cref="TimelineRenderer.Render"/> from <c>TimeAssertions</c> paired with
/// <c>MatchesSnapshot()</c> from <c>SnapshotAssertions.TUnit</c> against a committed baseline.
/// </summary>
/// <remarks>
/// The two packages share no PackageReference: <c>TimeAssertions.TUnit</c> does not depend on
/// <c>SnapshotAssertions.TUnit</c>. This test project adds both as consumer-side dependencies
/// to validate the pairing the same way a Fizyr3-shaped consumer would. A baseline drift on
/// either side (renderer format change, snapshot framework change) surfaces here before it
/// reaches downstream consumers.
/// </remarks>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class TimelineRendererSnapshotTests
{
    /// <summary>
    /// Pins the rendered timeline of a small fixed event sequence against the committed
    /// <c>TimelineRenderedSequence.expected.txt</c> baseline. The baseline is the canonical
    /// shape consumers will see: one event per line, <c>+{deltaMs}ms label</c> format,
    /// trailing newline after the final entry.
    /// </summary>
    [Test]
    public async Task TimelineRendererProducesSnapshotMatchingBaseline(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var epoch = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            new TimelineEvent(epoch, "WarmUp"),
            new TimelineEvent(epoch.AddMilliseconds(500), "Pick"),
            new TimelineEvent(epoch.AddSeconds(2), "Place"),
            new TimelineEvent(epoch.AddSeconds(2).AddMilliseconds(500), "Cleanup"),
        };

        var rendered = TimelineRenderer.Render(epoch, events);

        await Assert.That(rendered).MatchesSnapshot("TimelineRenderedSequence");
    }
}
