using System;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions.Attributes;

namespace TimeAssertions.TUnit;

/// <summary>
/// Fluent assertions on <see cref="FakeTimeProvider"/> instances. The headline use case for
/// the family: tests that inject <see cref="FakeTimeProvider"/> into production code and
/// then call <see cref="FakeTimeProvider.Advance"/> to simulate time passing can verify
/// the resulting state with these assertions.
/// </summary>
/// <remarks>
/// The Microsoft-recommended pattern for testable time in modern .NET:
/// <list type="number">
/// <item>Production code accepts an optional <see cref="TimeProvider"/> parameter (defaults to <see cref="TimeProvider.System"/>).</item>
/// <item>Tests construct a <see cref="FakeTimeProvider"/> and inject it.</item>
/// <item>Tests call <c>fakeTime.Advance(TimeSpan)</c> or <c>fakeTime.SetUtcNow(...)</c> to drive time forward deterministically.</item>
/// <item>Tests assert that production code reacted correctly — including the <see cref="FakeTimeProvider"/>'s own state.</item>
/// </list>
/// </remarks>
public static class FakeTimeProviderAssertions
{
    /// <summary>Asserts that the <see cref="FakeTimeProvider"/>'s current time differs from
    /// its construction-time start by exactly the specified <paramref name="total"/>.
    /// Useful as a sanity check that <c>Advance</c> calls in test setup landed correctly.</summary>
    /// <param name="value">The fake time provider.</param>
    /// <param name="total">Expected total elapsed since construction.</param>
    [GenerateAssertion(ExpectationMessage = "to have advanced by total {total}", InlineMethodBody = true)]
    public static bool HasAdvanced(this FakeTimeProvider value, TimeSpan total)
    {
        ArgumentNullException.ThrowIfNull(value);
        return (value.GetUtcNow() - value.Start) == total;
    }

    /// <summary>Asserts that the <see cref="FakeTimeProvider"/>'s current time differs from
    /// its construction-time start by approximately <paramref name="total"/>, within
    /// <paramref name="tolerance"/>. Use this when production code performs additional
    /// internal Advance calls that you want to allow for without exact matching.</summary>
    [GenerateAssertion(ExpectationMessage = "to have advanced by approximately {total} within tolerance {tolerance}", InlineMethodBody = true)]
    public static bool HasAdvancedBy(this FakeTimeProvider value, TimeSpan total, TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(value);
        var elapsed = value.GetUtcNow() - value.Start;
        var diff = elapsed - total;
        var absDiff = diff < TimeSpan.Zero ? -diff : diff;
        return absDiff <= tolerance;
    }

    /// <summary>Asserts that <c>fakeTime.GetUtcNow()</c> equals <paramref name="expected"/>
    /// exactly. Useful for tests that snap the fake clock to a specific moment via
    /// <see cref="FakeTimeProvider.SetUtcNow"/> and want to confirm the snap.</summary>
    [GenerateAssertion(ExpectationMessage = "to have UTC now {expected}", InlineMethodBody = true)]
    public static bool HasUtcNow(this FakeTimeProvider value, DateTimeOffset expected)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.GetUtcNow() == expected;
    }

    /// <summary>Asserts that <c>fakeTime.GetUtcNow()</c> is within <paramref name="tolerance"/>
    /// of <paramref name="expected"/>. Useful when the expected moment is computed (e.g. from
    /// integer-truncated minute math or chained <c>Advance</c> calls with rounding) rather than
    /// a literal — avoids exact-match brittleness.</summary>
    [GenerateAssertion(ExpectationMessage = "to have UTC now approximately {expected} within tolerance {tolerance}", InlineMethodBody = true)]
    public static bool HasUtcNowApproximately(this FakeTimeProvider value, DateTimeOffset expected, TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(value);
        var diff = value.GetUtcNow() - expected;
        var absDiff = diff < TimeSpan.Zero ? -diff : diff;
        return absDiff <= tolerance;
    }
}
