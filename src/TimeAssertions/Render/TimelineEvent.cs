using System;

namespace TimeAssertions.Render;

/// <summary>A single event in a timeline: a non-null label paired with the moment it
/// occurred. Used by <see cref="TimelineRenderer.Render"/> to produce deterministic
/// snapshot-friendly text from a sequence of timestamped events.</summary>
/// <param name="Timestamp">The moment at which the event occurred. Compared against the
/// caller-supplied epoch to produce a relative delta in the rendered output.</param>
/// <param name="Label">A short, human-readable identifier for the event. Non-null by
/// contract; null is unsupported and produces undefined rendering.</param>
public readonly record struct TimelineEvent(DateTimeOffset Timestamp, string Label);
