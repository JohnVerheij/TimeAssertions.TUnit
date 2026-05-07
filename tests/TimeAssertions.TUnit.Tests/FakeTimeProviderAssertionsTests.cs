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
/// fluent assertion.</summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class FakeTimeProviderAssertionsTests
{
    [Test]
    public async Task HasAdvancedExactly_AfterAdvanceCall_PassesWithExactTotal(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task HasAdvancedExactly_AfterMultipleAdvances_TotalsCorrectly(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(2));
        fakeTime.Advance(TimeSpan.FromMinutes(3));

        await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task HasAdvancedExactly_NoAdvanceCalls_TotalIsZero(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();

        await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.Zero);
    }

    [Test]
    public async Task HasAdvancedExactly_WrongTotal_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(async () =>
        {
            await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(99));
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasAdvancedApproximately_WithinTolerance_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromSeconds(5));
        fakeTime.Advance(TimeSpan.FromMilliseconds(2));  // small drift

        await Assert.That(fakeTime).HasAdvancedApproximately(
            total: TimeSpan.FromSeconds(5),
            tolerance: TimeSpan.FromMilliseconds(10));
    }

    [Test]
    public async Task HasAdvancedApproximately_OutsideTolerance_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromSeconds(99));

        await Assert.That(async () =>
        {
            await Assert.That(fakeTime).HasAdvancedApproximately(
                total: TimeSpan.FromSeconds(5),
                tolerance: TimeSpan.FromMilliseconds(10));
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasAdvancedApproximately_NegativeDelta_HandledByAbsoluteTolerance(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        // The absolute-difference logic should treat under-shoot and over-shoot symmetrically.
        // Advance by less than the expected total — the |elapsed - total| is positive after abs.
        fakeTime.Advance(TimeSpan.FromMilliseconds(95));

        await Assert.That(fakeTime).HasAdvancedApproximately(
            total: TimeSpan.FromMilliseconds(100),
            tolerance: TimeSpan.FromMilliseconds(10));
    }

    /// <summary>
    /// Pins the negative-tolerance guard added on <c>HasAdvancedApproximately</c> (and the
    /// obsolete <c>HasAdvancedBy</c> alias). A negative tolerance can never succeed, so a
    /// caller passing one almost certainly has a bug; we want a fail-fast
    /// <see cref="ArgumentOutOfRangeException"/> rather than a silent assertion failure.
    /// </summary>
    [Test]
    public async Task HasAdvancedApproximately_NegativeTolerance_ThrowsArgumentOutOfRange(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();

        await Assert.That(() =>
            FakeTimeProviderAssertions.HasAdvancedApproximately(
                fakeTime, total: TimeSpan.FromSeconds(5), tolerance: TimeSpan.FromMilliseconds(-1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>Same guard on the obsolete <c>HasAdvancedBy</c> alias — kept aligned with the
    /// new name until v0.4.0 removes the alias.</summary>
    [Test]
    public async Task HasAdvancedBy_NegativeTolerance_ThrowsArgumentOutOfRange(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();

#pragma warning disable CS0618 // Legacy obsolete alias: tested intentionally to keep the guard covered until removal in v0.4.0.
        await Assert.That(() =>
            FakeTimeProviderAssertions.HasAdvancedBy(
                fakeTime, total: TimeSpan.FromSeconds(5), tolerance: TimeSpan.FromMilliseconds(-1)))
            .Throws<ArgumentOutOfRangeException>();
#pragma warning restore CS0618
    }

    /// <summary>Same guard on <c>HasUtcNowApproximately</c> — symmetric API surface.</summary>
    [Test]
    public async Task HasUtcNowApproximately_NegativeTolerance_ThrowsArgumentOutOfRange(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();

        await Assert.That(() =>
            FakeTimeProviderAssertions.HasUtcNowApproximately(
                fakeTime, expected: DateTimeOffset.UnixEpoch, tolerance: TimeSpan.FromMilliseconds(-1)))
            .Throws<ArgumentOutOfRangeException>();
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

    // Obsolete-alias contract: the v0.1.x names HasAdvanced / HasAdvancedBy must keep working
    // through v0.3.x (two-minor [Obsolete] cycle, dropped in v0.4.0). Tests below pin both
    // the runtime behaviour and the [Obsolete] attribute presence so a future rename can't
    // accidentally break consumers mid-cycle.
#pragma warning disable CS0618 // Type or member is obsolete — intentional regression check
    [Test]
    public async Task HasAdvanced_obsoleteAlias_StillWorks(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task HasAdvancedBy_obsoleteAlias_StillWorks(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromSeconds(5));

        await Assert.That(fakeTime).HasAdvancedBy(
            total: TimeSpan.FromSeconds(5),
            tolerance: TimeSpan.FromMilliseconds(10));
    }
#pragma warning restore CS0618

    [Test]
    public async Task HasAdvanced_HasObsoleteAttribute(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var method = typeof(FakeTimeProviderAssertions).GetMethod(
            nameof(FakeTimeProviderAssertions.HasAdvanced),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        await Assert.That(method).IsNotNull();
        var attr = method!.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false);
        await Assert.That(attr).HasSingleItem();
    }

    [Test]
    public async Task HasAdvancedBy_HasObsoleteAttribute(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var method = typeof(FakeTimeProviderAssertions).GetMethod(
            nameof(FakeTimeProviderAssertions.HasAdvancedBy),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        await Assert.That(method).IsNotNull();
        var attr = method!.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false);
        await Assert.That(attr).HasSingleItem();
    }
}
