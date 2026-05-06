using System;
using TUnit.Assertions.Attributes;

namespace TimeAssertions.TUnit;

/// <summary>
/// <see cref="TimeProvider"/>-aware <see cref="DateTimeOffset"/> assertions for
/// recency / past / future checks against a (possibly fake) clock. TUnit core already
/// provides <c>IsInPast()</c> / <c>IsInFuture()</c> using <see cref="DateTimeOffset.Now"/>;
/// these complement them with explicit <see cref="TimeProvider"/> injection so
/// <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/>-driven tests can verify
/// time-relative state deterministically.
/// </summary>
public static class DateTimeOffsetAssertions
{
    /// <summary>Asserts that the timestamp is within the last <paramref name="window"/>
    /// of the <see cref="TimeProvider"/>'s notion of "now". When
    /// <paramref name="timeProvider"/> is <see langword="null"/>,
    /// <see cref="TimeProvider.System"/> is used. Returns false for timestamps in the
    /// future relative to "now".</summary>
    /// <param name="value">The timestamp under test.</param>
    /// <param name="window">The recency window. Must be non-negative.</param>
    /// <param name="timeProvider">The time source to compare against, or
    /// <see langword="null"/> to use <see cref="TimeProvider.System"/>.</param>
    [GenerateAssertion(ExpectationMessage = "to be within the last {window} relative to the supplied time provider", InlineMethodBody = true)]
    public static bool IsRecent(this DateTimeOffset value, TimeSpan window, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var elapsed = now - value;
        return elapsed >= TimeSpan.Zero && elapsed <= window;
    }

    /// <summary>Asserts that the timestamp is strictly before the
    /// <see cref="TimeProvider"/>'s notion of "now". Distinct from TUnit core's
    /// <c>IsInPast()</c> in that the comparison source is explicit (suitable for
    /// <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/>-driven tests).</summary>
    [GenerateAssertion(ExpectationMessage = "to be strictly before the supplied time provider's UTC now", InlineMethodBody = true)]
    public static bool IsBeforeNow(this DateTimeOffset value, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return value < timeProvider.GetUtcNow();
    }

    /// <summary>Asserts that the timestamp is strictly after the
    /// <see cref="TimeProvider"/>'s notion of "now". Distinct from TUnit core's
    /// <c>IsInFuture()</c> in that the comparison source is explicit (suitable for
    /// <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/>-driven tests).</summary>
    [GenerateAssertion(ExpectationMessage = "to be strictly after the supplied time provider's UTC now", InlineMethodBody = true)]
    public static bool IsAfterNow(this DateTimeOffset value, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return value > timeProvider.GetUtcNow();
    }
}
