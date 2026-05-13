using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions.Render;

namespace TimeAssertions.TUnit.Tests;

/// <summary>
/// Coverage-instrumentation exercise for <see cref="TimelineRenderer.Render"/>. The
/// authoritative contract tests live in <c>tests/TimeAssertions.Tests/TimelineRendererTests.cs</c>
/// — that project is framework-agnostic (no <c>TimeAssertions.TUnit</c> reference) so the
/// renderer's framework-independence is structurally enforced. The CI coverage gate, however,
/// instruments only this project's test exe; the renderer's lines sit in <c>TimeAssertions.dll</c>
/// and would show as uncovered without a touchpoint here. Each branch of <see cref="TimelineRenderer.Render"/>
/// — empty input, single event, multi-event ordering, negative delta, duplicate timestamps,
/// and null-events argument validation — is exercised once below so the production assembly's
/// coverage rate reflects the actual test depth.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class TimelineRendererCoverageExercise
{
    private static readonly DateTimeOffset Epoch = new(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ExercisesAllBranches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Empty
        await Assert.That(TimelineRenderer.Render(Epoch, [])).IsEqualTo(string.Empty);

        // Single event at epoch
        var single = TimelineRenderer.Render(Epoch, [new TimelineEvent(Epoch, "Start")]);
        await Assert.That(single).IsEqualTo("+0ms Start" + Environment.NewLine);

        // Multiple ascending events
        var multi = TimelineRenderer.Render(Epoch, new[]
        {
            new TimelineEvent(Epoch, "A"),
            new TimelineEvent(Epoch.AddMilliseconds(100), "B"),
        });
        await Assert.That(multi).IsEqualTo("+0ms A" + Environment.NewLine + "+100ms B" + Environment.NewLine);

        // Negative delta (event before epoch)
        var negative = TimelineRenderer.Render(Epoch, [new TimelineEvent(Epoch.AddMilliseconds(-50), "Pre")]);
        await Assert.That(negative).IsEqualTo("-50ms Pre" + Environment.NewLine);

        // Duplicate timestamps: input order preserved
        var dup = TimelineRenderer.Render(Epoch, new[]
        {
            new TimelineEvent(Epoch, "First"),
            new TimelineEvent(Epoch, "Second"),
        });
        await Assert.That(dup).IsEqualTo("+0ms First" + Environment.NewLine + "+0ms Second" + Environment.NewLine);
    }

    [Test]
    public async Task NullEvents_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await Assert.That(() => TimelineRenderer.Render(Epoch, null!))
            .Throws<ArgumentNullException>();
    }
}
