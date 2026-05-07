namespace Smoke.Consumer;

/// <summary>
/// Smoke tests proving that an external consumer can adopt TimeAssertions.TUnit purely via
/// the README's recommended GlobalUsings.cs snippet — no extra <c>using TimeAssertions.TUnit;</c>
/// directive at every call site, no other wiring. The test class lives in <c>Smoke.Consumer</c>
/// deliberately: TimeAssertions.TUnit's own test project is in the
/// <c>TimeAssertions.TUnit.Tests</c> namespace, which inherits parent-namespace visibility into
/// <c>TimeAssertions.TUnit</c> — that inheritance would mask any future namespace-resolution
/// bug in the source-generated entry points. By placing this file in a namespace with NO parent
/// relationship to TimeAssertions.TUnit, this project is the canonical regression coverage for
/// the resolution-pathway bug class.
/// </summary>
[Category("ConsumerSurface")]
[Timeout(10_000)]
internal sealed class ConsumerSurfaceSmokeTests
{
    /// <summary>Pins that <c>HasAdvancedExactly</c> resolves cleanly for an external consumer
    /// using only the README's GlobalUsings snippet. Source-generator-emitted entry point in
    /// <c>TUnit.Assertions.Extensions</c>; auto-imports alongside <c>Assert.That</c>.</summary>
    [Test]
    public async Task HasAdvancedExactlyResolvesAndPassesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(5));
    }

    /// <summary>Pins that <c>HasAdvancedApproximately</c> resolves and the absolute-tolerance
    /// path runs. Renamed from <c>HasAdvancedBy</c> in v0.2.0; the new name is the canonical
    /// public surface this smoke test guards.</summary>
    [Test]
    public async Task HasAdvancedApproximatelyResolvesAndPassesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider();
        fakeTime.Advance(TimeSpan.FromSeconds(5));

        await Assert.That(fakeTime).HasAdvancedApproximately(
            total: TimeSpan.FromSeconds(5),
            tolerance: TimeSpan.FromMilliseconds(10));
    }

    /// <summary>Pins that <c>HasUtcNow</c> + <c>HasUtcNowApproximately</c> both resolve.</summary>
    [Test]
    public async Task HasUtcNowEntryPointsResolveAndPassAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapTo = new DateTimeOffset(2026, 5, 7, 18, 30, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(snapTo);

        await Assert.That(fakeTime).HasUtcNow(snapTo);
        await Assert.That(fakeTime).HasUtcNowApproximately(snapTo, TimeSpan.FromMilliseconds(10));
    }

    /// <summary>Pins that the <c>TimeProvider</c>-aware <c>DateTimeOffset</c> assertions
    /// (<c>IsRecent</c> / <c>IsBeforeNow</c> / <c>IsAfterNow</c>) resolve cleanly.</summary>
    [Test]
    public async Task DateTimeOffsetEntryPointsResolveAndPassAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var anchor = new DateTimeOffset(2026, 5, 7, 18, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(anchor);

        await Assert.That(anchor.AddSeconds(-1)).IsRecent(TimeSpan.FromSeconds(5), fakeTime);
        await Assert.That(anchor.AddMinutes(-5)).IsBeforeNow(fakeTime);
        await Assert.That(anchor.AddMinutes(5)).IsAfterNow(fakeTime);
    }

    /// <summary>Pins that the cross-cutting <c>WithinTimeBudget</c> chain resolves via
    /// <c>.And</c> after a behavioural assertion, the canonical pattern documented in the
    /// README.</summary>
    [Test]
    public async Task WithinTimeBudgetResolvesAndPassesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(FastValueAsync(ct))
            .IsEqualTo(42)
            .And.WithinTimeBudget(TimeSpan.FromSeconds(5));
    }

    /// <summary>Pins that the v0.2.0 <c>WithinTimeBudgetCapturing</c> chain resolves and the
    /// capture callback receives the measured elapsed.</summary>
    [Test]
    public async Task WithinTimeBudgetCapturingResolvesAndInvokesCaptureAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var captured = TimeSpan.Zero;
        // Sentinel: prove the callback ACTUALLY ran. Without this, asserting only on
        // `captured >= TimeSpan.Zero` would pass even if the callback never fired (the
        // initial Zero satisfies the inequality), so the smoke test would not actually
        // protect the capture-callback contract.
        var captureInvoked = false;
        await Assert.That(FastValueAsync(ct))
            .IsEqualTo(42)
            .And.WithinTimeBudgetCapturing(TimeSpan.FromSeconds(5), e => { captured = e; captureInvoked = true; });

        await Assert.That(captureInvoked).IsTrue();
        await Assert.That(captured).IsGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    private static async Task<int> FastValueAsync(CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return 42;
    }
}
