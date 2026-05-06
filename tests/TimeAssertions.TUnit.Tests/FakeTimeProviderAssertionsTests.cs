using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions.TUnit;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>End-to-end tests for the <see cref="FakeTimeProvider"/> assertions. Each test
/// constructs a fresh <see cref="FakeTimeProvider"/>, drives time via
/// <c>Advance</c> / <c>SetUtcNow</c>, and verifies the resulting state via the
/// fluent assertion. Mirrors the canonical AWL test pattern for testable time.</summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class FakeTimeProviderAssertionsTests
{
    [Test]
    public async Task HasAdvanced_AfterAdvanceCall_PassesWithExactTotal(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task HasAdvanced_AfterMultipleAdvances_TotalsCorrectly(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(2));
        fakeTime.Advance(TimeSpan.FromMinutes(3));

        await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task HasAdvanced_NoAdvanceCalls_TotalIsZero(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();

        await Assert.That(fakeTime).HasAdvanced(TimeSpan.Zero);
    }

    [Test]
    public async Task HasAdvanced_WrongTotal_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(async () =>
        {
            await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromMinutes(99));
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasAdvancedBy_WithinTolerance_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromSeconds(5));
        fakeTime.Advance(TimeSpan.FromMilliseconds(2));  // small drift

        await Assert.That(fakeTime).HasAdvancedBy(
            total: TimeSpan.FromSeconds(5),
            tolerance: TimeSpan.FromMilliseconds(10));
    }

    [Test]
    public async Task HasAdvancedBy_OutsideTolerance_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromSeconds(99));

        await Assert.That(async () =>
        {
            await Assert.That(fakeTime).HasAdvancedBy(
                total: TimeSpan.FromSeconds(5),
                tolerance: TimeSpan.FromMilliseconds(10));
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasAdvancedBy_NegativeDelta_HandledByAbsoluteTolerance(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        // The absolute-difference logic should treat under-shoot and over-shoot symmetrically.
        // Advance by less than the expected total — the |elapsed - total| is positive after abs.
        fakeTime.Advance(TimeSpan.FromMilliseconds(95));

        await Assert.That(fakeTime).HasAdvancedBy(
            total: TimeSpan.FromMilliseconds(100),
            tolerance: TimeSpan.FromMilliseconds(10));
    }

    [Test]
    public async Task HasUtcNow_AfterSetUtcNow_PassesAtExactMoment(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapTo = new DateTimeOffset(2026, 5, 6, 18, 30, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(snapTo);

        await Assert.That(fakeTime).HasUtcNow(snapTo);
    }

    [Test]
    public async Task HasUtcNow_DifferentMoment_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapTo = new DateTimeOffset(2026, 5, 6, 18, 30, 0, TimeSpan.Zero);
        var different = new DateTimeOffset(2026, 5, 6, 18, 31, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(snapTo);

        await Assert.That(async () =>
        {
            await Assert.That(fakeTime).HasUtcNow(different);
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasUtcNowApproximately_WithinTolerance_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var expected = new DateTimeOffset(2026, 5, 6, 18, 30, 0, TimeSpan.Zero);
        var actual = expected.AddMilliseconds(3);
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(actual);

        await Assert.That(fakeTime).HasUtcNowApproximately(expected, TimeSpan.FromMilliseconds(10));
    }

    [Test]
    public async Task HasUtcNowApproximately_OutsideTolerance_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var expected = new DateTimeOffset(2026, 5, 6, 18, 30, 0, TimeSpan.Zero);
        var actual = expected.AddSeconds(5);
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(actual);

        await Assert.That(async () =>
        {
            await Assert.That(fakeTime).HasUtcNowApproximately(expected, TimeSpan.FromMilliseconds(10));
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasUtcNowApproximately_NegativeDelta_HandledByAbsoluteTolerance(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var expected = new DateTimeOffset(2026, 5, 6, 18, 30, 0, TimeSpan.Zero);
        var actual = expected.AddMilliseconds(-3);
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(actual);

        await Assert.That(fakeTime).HasUtcNowApproximately(expected, TimeSpan.FromMilliseconds(10));
    }

}
