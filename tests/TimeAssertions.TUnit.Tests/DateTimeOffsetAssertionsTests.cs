using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions.TUnit;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>End-to-end tests for the <see cref="TimeProvider"/>-aware
/// <see cref="DateTimeOffset"/> assertions. Each test constructs a
/// <see cref="FakeTimeProvider"/>, snaps it to a deterministic moment, and verifies
/// recency / past / future semantics relative to that fake clock.</summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class DateTimeOffsetAssertionsTests
{
    private static readonly DateTimeOffset FakeNow = new(2026, 5, 6, 18, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider CreateFakeAt(DateTimeOffset now)
    {
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(now);
        return fakeTime;
    }

    // === IsRecent ===

    [Test]
    public async Task IsRecent_TimestampWithinWindow_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        var fiveMinutesAgo = FakeNow.AddMinutes(-5);

        await Assert.That(fiveMinutesAgo).IsRecent(TimeSpan.FromMinutes(10), fakeTime);
    }

    [Test]
    public async Task IsRecent_TimestampOutsideWindow_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        var oneHourAgo = FakeNow.AddHours(-1);

        await Assert.That(async () =>
        {
            await Assert.That(oneHourAgo).IsRecent(TimeSpan.FromMinutes(10), fakeTime);
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task IsRecent_TimestampInFuture_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        var inFuture = FakeNow.AddMinutes(5);

        // Even within the window magnitude, future timestamps don't qualify as "recent".
        await Assert.That(async () =>
        {
            await Assert.That(inFuture).IsRecent(TimeSpan.FromMinutes(10), fakeTime);
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task IsRecent_NoTimeProvider_UsesSystemTimeProvider(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var oneSecondAgo = TimeProvider.System.GetUtcNow().AddSeconds(-1);

        await Assert.That(oneSecondAgo).IsRecent(TimeSpan.FromSeconds(60));
    }

    // === IsBeforeNow ===

    [Test]
    public async Task IsBeforeNow_PastTimestamp_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        var pastTimestamp = FakeNow.AddDays(-1);

        await Assert.That(pastTimestamp).IsBeforeNow(fakeTime);
    }

    [Test]
    public async Task IsBeforeNow_FutureTimestamp_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        var futureTimestamp = FakeNow.AddDays(1);

        await Assert.That(async () =>
        {
            await Assert.That(futureTimestamp).IsBeforeNow(fakeTime);
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task IsBeforeNow_AtExactNow_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        // "Strictly before": exact equality fails.
        await Assert.That(async () =>
        {
            await Assert.That(FakeNow).IsBeforeNow(fakeTime);
        }).Throws<AssertionException>();
    }

    // === IsAfterNow ===

    [Test]
    public async Task IsAfterNow_FutureTimestamp_Passes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        var futureTimestamp = FakeNow.AddDays(1);

        await Assert.That(futureTimestamp).IsAfterNow(fakeTime);
    }

    [Test]
    public async Task IsAfterNow_PastTimestamp_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = CreateFakeAt(FakeNow);
        var pastTimestamp = FakeNow.AddDays(-1);

        await Assert.That(async () =>
        {
            await Assert.That(pastTimestamp).IsAfterNow(fakeTime);
        }).Throws<AssertionException>();
    }

}
