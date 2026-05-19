using System;
using System.Collections.Generic;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// Fluent rate-limit assertions on a chronologically-ordered sequence of invocation
/// timestamps. The headline use case is a periodic-probe contract: "the failure handler
/// must fire at most once per 30 seconds; subsequent failures inside that window are
/// suppressed".
/// </summary>
/// <remarks>
/// <para>
/// The receiver is the recorded invocation log itself, not the action being invoked.
/// Consumer test code records timestamps via whatever instrumentation is natural (a
/// captured <c>FakeLogCollector</c>'s log records, an explicit
/// <see cref="List{T}"/> populated in a wrapped callback, etc.) and asserts the log.
/// </para>
/// <para>
/// The assertion preserves input order verbatim. Caller is responsible for sorting if
/// the underlying mechanism does not guarantee chronological order. Empty and
/// single-element sequences pass trivially: no consecutive pair exists to violate.
/// </para>
/// </remarks>
public static class RateLimitAssertions
{
    /// <summary>
    /// Asserts that consecutive timestamps in the supplied sequence maintain at least
    /// the specified minimum interval. The first violating consecutive pair fails the
    /// assertion with a message naming the violating index, the observed gap, and the
    /// required minimum.
    /// </summary>
    /// <remarks>
    /// <para>The boundary case <c>gap == interval</c> passes (the minimum is
    /// inclusive). The violation predicate is <c>gap &lt; interval</c>, so any
    /// non-decreasing sequence with <paramref name="interval"/> of
    /// <see cref="TimeSpan.Zero"/> trivially passes (including duplicate-timestamp
    /// pairs whose gap is exactly zero). Strictly out-of-order pairs (negative gap)
    /// fail at any non-negative interval.</para>
    /// </remarks>
    /// <param name="timestamps">The recorded invocation timestamps in chronological
    /// order. The first violating pair is reported by index; reporting stops at the
    /// first violation.</param>
    /// <param name="interval">The minimum allowed gap between consecutive invocations.
    /// Must be non-negative.</param>
    /// <returns>
    /// <see cref="AssertionResult.Passed"/> when every consecutive pair is at least
    /// <paramref name="interval"/> apart, including the trivially-passing empty and
    /// single-element cases. Otherwise <see cref="AssertionResult.Failed(string)"/>
    /// with a message naming the first violation.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="timestamps"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is
    /// negative.</exception>
    [GenerateAssertion(InlineMethodBody = false)]
    public static AssertionResult WasInvokedAtMostOncePer(
        this IReadOnlyList<DateTimeOffset> timestamps,
        TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);

        for (var i = 1; i < timestamps.Count; i++)
        {
            var gap = timestamps[i] - timestamps[i - 1];
            if (gap < interval)
            {
                return AssertionResult.Failed(
                    TimeRenderingHelpers.FormatRateLimitViolation(timestamps, i, gap, interval));
            }
        }

        return AssertionResult.Passed;
    }
}
