using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions.Render;

namespace TimeAssertions.Tests;

/// <summary>Pins the rendered format produced by <see cref="TimelineRenderer.Render"/>.
/// Each test fixes one corner of the contract: empty input, single line, multi-line ordered,
/// negative-delta, and duplicate-timestamp input-order preservation. Snapshot consumers
/// downstream rely on the exact byte shape of these outputs; changing them is a breaking
/// change.</summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class TimelineRendererTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The literal LF byte the renderer emits between events. Hardcoded here so
    /// these tests assert against the same cross-platform-deterministic byte sequence the
    /// renderer produces (see <c>TimelineRenderer</c> XML docs for the rationale).</summary>
    private const string Lf = "\n";

    /// <summary>Empty input renders as <see cref="string.Empty"/>: zero bytes, no trailing
    /// newline. Distinct from the single-event-at-epoch case (which renders a single line
    /// with a trailing newline). Snapshot files for empty timelines are therefore
    /// byte-empty.</summary>
    [Test]
    public async Task Render_EmptyEvents_RendersEmpty(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rendered = TimelineRenderer.Render(Epoch, []);

        await Assert.That(rendered).IsEqualTo(string.Empty);
    }

    /// <summary>A single event at the epoch renders as <c>+0ms label\n</c>. Fixes the
    /// trailing-newline contract and the zero-delta plus-prefix.</summary>
    [Test]
    public async Task Render_SingleEvent_RendersOneLine(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rendered = TimelineRenderer.Render(Epoch, [new TimelineEvent(Epoch, "Start")]);

        await Assert.That(rendered).IsEqualTo("+0ms Start" + Lf);
    }

    /// <summary>Multiple ascending events render in input order with one newline-terminated
    /// line per event. Pins the canonical use case: pre-sorted heartbeat / ping-loop
    /// timelines pinned via snapshot.</summary>
    [Test]
    public async Task Render_MultipleEvents_RendersAllInOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var events = new[]
        {
            new TimelineEvent(Epoch, "WarmUp"),
            new TimelineEvent(Epoch.AddMilliseconds(500), "Pick"),
            new TimelineEvent(Epoch.AddSeconds(2), "Place"),
        };
        var expected = "+0ms WarmUp" + Lf
                     + "+500ms Pick" + Lf
                     + "+2000ms Place" + Lf;

        var rendered = TimelineRenderer.Render(Epoch, events);

        await Assert.That(rendered).IsEqualTo(expected);
    }

    /// <summary>An event before the epoch renders with a leading minus sign and no plus.
    /// Useful for snapshots where the epoch is the moment of interest and earlier events
    /// are pre-trigger context.</summary>
    [Test]
    public async Task Render_NegativeDelta_RendersNegativeMs(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var earlier = Epoch.AddMilliseconds(-250);
        var rendered = TimelineRenderer.Render(Epoch, [new TimelineEvent(earlier, "Trigger")]);

        await Assert.That(rendered).IsEqualTo("-250ms Trigger" + Lf);
    }

    /// <summary>Two events at exactly the same <see cref="TimelineEvent.Timestamp"/> render
    /// in input order, both on their own lines. The renderer never re-orders, drops, or
    /// merges duplicates: ordering of ties is the caller's responsibility.</summary>
    [Test]
    public async Task Render_DuplicateTimestamps_RendersBothInInputOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var at = Epoch.AddSeconds(1);
        var events = new[]
        {
            new TimelineEvent(at, "First"),
            new TimelineEvent(at, "Second"),
        };
        var expected = "+1000ms First" + Lf
                     + "+1000ms Second" + Lf;

        var rendered = TimelineRenderer.Render(Epoch, events);

        await Assert.That(rendered).IsEqualTo(expected);
    }

    /// <summary>Argument validation: <see langword="null"/> events list throws
    /// <see cref="ArgumentNullException"/>, not a silent empty render.</summary>
    [Test]
    public async Task Render_NullEvents_Throws(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await Assert.That(() => TimelineRenderer.Render(Epoch, null!))
            .Throws<ArgumentNullException>();
    }
}
