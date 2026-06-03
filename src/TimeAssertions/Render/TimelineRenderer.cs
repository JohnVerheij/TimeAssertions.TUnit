using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TimeAssertions.Render;

/// <summary>
/// Pure renderer that converts a sequence of <see cref="TimelineEvent"/> into deterministic
/// multi-line text suitable for snapshot comparison. Each event renders as
/// <c>+{deltaMs}ms label</c>, where <c>deltaMs</c> is the millisecond offset from the
/// caller-supplied epoch (negative deltas render with a leading minus sign and no plus).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pairs naturally with snapshot assertions.</b> Render the timeline once, then pin
/// the result against a baseline:
/// </para>
/// <code>
/// var rendered = TimelineRenderer.Render(epoch, events);
/// await Assert.That(rendered).MatchesSnapshot();
/// </code>
/// <para>
/// The <c>MatchesSnapshot()</c> extension above lives in the sibling
/// <c>SnapshotAssertions.TUnit</c> package; this package does not depend on it. The
/// two-line composition is deliberate: it lets consumers reach for the renderer without
/// committing to a specific snapshot framework, and lets the SnapshotAssertions package
/// stay an opt-in pairing rather than a transitive dependency.
/// </para>
/// <para>
/// <b>Caller sorts.</b> The renderer preserves input order verbatim, including ties on
/// <see cref="TimelineEvent.Timestamp"/>. If the snapshot needs a specific ordering
/// (chronological, by-category, etc.) the caller sorts the input list first.
/// </para>
/// <para>
/// <b>Allocation-conscious.</b> A <see cref="StringBuilder"/> with capacity precomputed
/// from the input count avoids the resize cascade that would surface on large timelines.
/// All numeric formatting uses <see cref="CultureInfo.InvariantCulture"/> to keep
/// snapshots stable across locales.
/// </para>
/// <para>
/// <b>Deterministic line endings.</b> Lines are terminated with the literal LF byte
/// (<c>'\n'</c>), not <see cref="Environment.NewLine"/>. The CRLF /
/// LF split between Windows and Unix would break the "byte-stable output" contract:
/// the same timeline would serialise differently per OS, producing snapshot mismatches
/// on cross-platform CI. Hardcoding LF keeps snapshots committed on one platform
/// compatible with test runs on every other.
/// </para>
/// </remarks>
public static class TimelineRenderer
{
    /// <summary>Renders <paramref name="events"/> as a deterministic, snapshot-friendly
    /// multi-line string, one event per line, deltas relative to <paramref name="epoch"/>.</summary>
    /// <param name="epoch">The zero-moment against which every event's
    /// <see cref="TimelineEvent.Timestamp"/> is rendered as a relative delta.</param>
    /// <param name="events">The events to render, in the order they should appear. Empty
    /// input produces <see cref="string.Empty"/>.</param>
    /// <returns>A multi-line string with one <c>+{deltaMs}ms label</c> line per event, or
    /// <see cref="string.Empty"/> when <paramref name="events"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <see langword="null"/>.</exception>
    public static string Render(DateTimeOffset epoch, IReadOnlyList<TimelineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count is 0)
        {
            return string.Empty;
        }

        // Average rendered line: '+' + up to 10 digit delta + 'ms ' + ~10-char label + newline.
        // 32 bytes per line is a conservative upper-bound for the common heartbeat/ping-loop shape.
        var sb = new StringBuilder(capacity: events.Count * 32);
        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            var deltaMs = (ev.Timestamp - epoch).TotalMilliseconds;
            var prefix = deltaMs >= 0 ? "+" : string.Empty;
            sb.Append(CultureInfo.InvariantCulture, $"{prefix}{deltaMs:F0}ms {ev.Label}\n");
        }
        return sb.ToString();
    }
}
