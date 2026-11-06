using System;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// Fluent assertions on <see cref="FakeTimeProvider"/> instances. The headline use case for
/// the family: tests that inject <see cref="FakeTimeProvider"/> into production code and
/// then call <see cref="FakeTimeProvider.Advance"/> to simulate time passing can verify
/// the resulting state with these assertions.
/// </summary>
/// <remarks>
/// <para>
/// The Microsoft-recommended pattern for testable time in modern .NET:
/// </para>
/// <list type="number">
/// <item>Production code accepts an optional <see cref="TimeProvider"/> parameter (defaults to <see cref="TimeProvider.System"/>).</item>
/// <item>Tests construct a <see cref="FakeTimeProvider"/> and inject it.</item>
/// <item>Tests call <c>fakeTime.Advance(TimeSpan)</c> or <c>fakeTime.SetUtcNow(...)</c> to drive time forward deterministically.</item>
/// <item>Tests assert that production code reacted correctly: including the <see cref="FakeTimeProvider"/>'s own state.</item>
/// </list>
/// <para>
/// On failure these assertions say how far the clock advanced, or where it stands, with times in the invariant
/// round-trip format and durations in the invariant <c>c</c> format, so the message reads the same on every machine.
/// </para>
/// </remarks>
public static class FakeTimeProviderAssertions
{
    /// <summary>Asserts that the <see cref="FakeTimeProvider"/>'s current time differs from
    /// its construction-time start by exactly the specified <paramref name="total"/>.
    /// Useful as a sanity check that <c>Advance</c> calls in test setup landed correctly.</summary>
    /// <param name="value">The fake time provider.</param>
    /// <param name="total">Expected total elapsed since construction.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the clock advanced by exactly <paramref name="total"/>;
    /// otherwise <see cref="AssertionResult.Failed(string)"/> naming how far it advanced, the difference, and the
    /// start and current times.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    [GenerateAssertion(ExpectationMessage = "to have advanced by total {total}", InlineMethodBody = false)]
    public static AssertionResult HasAdvancedExactly(this FakeTimeProvider value, TimeSpan total)
    {
        ArgumentNullException.ThrowIfNull(value);
        var advanced = value.GetUtcNow() - value.Start;
        return advanced == total ? AssertionResult.Passed : AssertionResult.Failed(DescribeAdvance(value, advanced, total));
    }

    /// <summary>Asserts that the <see cref="FakeTimeProvider"/>'s current time differs from
    /// its construction-time start by approximately <paramref name="total"/>, within
    /// <paramref name="tolerance"/>. Use this when production code performs additional
    /// internal Advance calls that you want to allow for without exact matching.</summary>
    /// <param name="value">The fake time provider.</param>
    /// <param name="total">Expected total elapsed since construction.</param>
    /// <param name="tolerance">The allowed absolute tolerance around <paramref name="total"/>. Must be non-negative.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the clock advanced by <paramref name="total"/> within
    /// <paramref name="tolerance"/>; otherwise <see cref="AssertionResult.Failed(string)"/> naming how far it
    /// advanced, the difference, and the start and current times.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    [GenerateAssertion(ExpectationMessage = "to have advanced by approximately {total} within tolerance {tolerance}", InlineMethodBody = false)]
    public static AssertionResult HasAdvancedApproximately(this FakeTimeProvider value, TimeSpan total, TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);
        var advanced = value.GetUtcNow() - value.Start;
        return (advanced - total).Duration() <= tolerance
            ? AssertionResult.Passed
            : AssertionResult.Failed(DescribeAdvance(value, advanced, total));
    }

    /// <summary>Asserts that <c>fakeTime.GetUtcNow()</c> equals <paramref name="expected"/>
    /// exactly. Useful for tests that snap the fake clock to a specific moment via
    /// <see cref="FakeTimeProvider.SetUtcNow"/> and want to confirm the snap.</summary>
    /// <param name="value">The fake time provider.</param>
    /// <param name="expected">The expected current time.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the clock reads <paramref name="expected"/>; otherwise
    /// <see cref="AssertionResult.Failed(string)"/> naming both times and the difference.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    [GenerateAssertion(ExpectationMessage = "to have UTC now equal to the expected time", InlineMethodBody = false)]
    public static AssertionResult HasUtcNow(this FakeTimeProvider value, DateTimeOffset expected)
    {
        ArgumentNullException.ThrowIfNull(value);
        var now = value.GetUtcNow();
        return now == expected ? AssertionResult.Passed : AssertionResult.Failed(DescribeNow(now, expected));
    }

    /// <summary>Asserts that <c>fakeTime.GetUtcNow()</c> is within <paramref name="tolerance"/>
    /// of <paramref name="expected"/>. Useful when the expected moment is computed (e.g. from
    /// integer-truncated minute math or chained <c>Advance</c> calls with rounding) rather than
    /// a literal: avoids exact-match brittleness.</summary>
    /// <param name="value">The fake time provider.</param>
    /// <param name="expected">The expected current time.</param>
    /// <param name="tolerance">The allowed absolute tolerance around <paramref name="expected"/>. Must be non-negative.</param>
    /// <returns><see cref="AssertionResult.Passed"/> when the clock reads <paramref name="expected"/> within
    /// <paramref name="tolerance"/>; otherwise <see cref="AssertionResult.Failed(string)"/> naming both times and
    /// the difference.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    [GenerateAssertion(ExpectationMessage = "to have UTC now within tolerance {tolerance} of the expected time", InlineMethodBody = false)]
    public static AssertionResult HasUtcNowApproximately(this FakeTimeProvider value, DateTimeOffset expected, TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);
        var now = value.GetUtcNow();
        return (now - expected).Duration() <= tolerance
            ? AssertionResult.Passed
            : AssertionResult.Failed(DescribeNow(now, expected));
    }

    /// <summary>Describes how far the clock advanced: "it advanced by 00:30:00, off by 00:01:00 (start ..., now ...)".</summary>
    /// <param name="value">The fake time provider.</param>
    /// <param name="advanced">How far the clock advanced since its start.</param>
    /// <param name="total">The expected advance.</param>
    /// <returns>The failure text.</returns>
    private static string DescribeAdvance(FakeTimeProvider value, TimeSpan advanced, TimeSpan total) =>
        $"it advanced by {advanced}, off by {(advanced - total).Duration()} (start {value.Start:O}, now {value.GetUtcNow():O})";

    /// <summary>Describes where the clock stands: "it was ..., expected ..., off by ...".</summary>
    /// <param name="now">The clock's current time.</param>
    /// <param name="expected">The expected time.</param>
    /// <returns>The failure text.</returns>
    private static string DescribeNow(DateTimeOffset now, DateTimeOffset expected) =>
        $"it was {now:O}, expected {expected:O}, off by {(now - expected).Duration()}";
}
