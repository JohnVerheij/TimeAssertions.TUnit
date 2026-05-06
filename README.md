# TimeAssertions.TUnit

> **Scope:** Test projects only. Not intended for production code.

**The TUnit assertion package for projects committed to `TimeProvider`-based testable time.**

If your production code accepts a `TimeProvider` parameter (Microsoft's recommended pattern since .NET 8) and your tests inject `FakeTimeProvider` to drive time deterministically, this package is the bridge: fluent assertions on `FakeTimeProvider` state, `TimeProvider`-aware `DateTimeOffset` recency / past / future checks, plus the cross-cutting `.WithinTimeBudget(TimeSpan)` chain extension for assertion-level timing budgets.

```csharp
using Microsoft.Extensions.Time.Testing;

[Test]
public async Task PreReleaseExpiration_advances_state_after_clock_moves_forward()
{
    var fakeTime = new FakeTimeProvider();
    var service = new ExpirationService(fakeTime);

    fakeTime.Advance(TimeSpan.FromMinutes(31));
    await service.ProcessAsync(CancellationToken.None);

    using (Assert.Multiple())
    {
        await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromMinutes(31));
        await Assert.That(service.LastProcessedAt).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
        await Assert.That(service.NextRunAt).IsAfterNow(fakeTime);
    }
}
```

## Why TimeProvider in tests

The Microsoft-recommended pattern for testable time in modern .NET (since .NET 8):

1. **Production code accepts an optional `TimeProvider` parameter** (defaults to `TimeProvider.System`).
2. **Tests construct a `FakeTimeProvider`** and inject it instead.
3. **Tests call `fakeTime.Advance(TimeSpan)` or `fakeTime.SetUtcNow(...)`** to drive time forward deterministically — no `Thread.Sleep`, no flaky timing, no waiting for real wall-clock seconds to pass.
4. **Tests assert** that production code reacted correctly to the simulated time.

This package supplies the assertion side of step 4. Without it, you write boilerplate (`Assert.True(fakeTime.GetUtcNow() == expected, ...)`) for every time-dependent test. With it:

```csharp
await Assert.That(fakeTime).HasUtcNow(expected);
await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromMinutes(5));
await Assert.That(timestamp).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
await Assert.That(timestamp).IsBeforeNow(fakeTime);
await Assert.That(timestamp).IsAfterNow(fakeTime);
```

For projects standardising on this pattern, TimeAssertions.TUnit is the TUnit-side test infrastructure that pays for itself test-by-test.

## What 0.1.0 ships

| Group | Methods | Notes |
|---|---|---|
| `FakeTimeProvider` state | `HasAdvanced(TimeSpan)`, `HasAdvancedBy(TimeSpan, TimeSpan tolerance)`, `HasUtcNow(DateTimeOffset)`, `HasUtcNowApproximately(DateTimeOffset, TimeSpan tolerance)` | The headline FakeTimeProvider integration |
| `DateTimeOffset` (`TimeProvider`-aware) | `IsRecent(TimeSpan window, TimeProvider? = null)`, `IsBeforeNow(TimeProvider)`, `IsAfterNow(TimeProvider)` | Use any `TimeProvider`; `IsRecent` defaults to `TimeProvider.System` |
| Cross-cutting timing budget | `.And.WithinTimeBudget(TimeSpan)` after any behavioural assertion | Post-facto check on TUnit's evaluator wall-clock |

`Microsoft.Extensions.TimeProvider.Testing` is propagated as a transitive dependency, so `FakeTimeProvider` is available in any consuming test project without an extra explicit reference.

## Quick start

```bash
dotnet add package TimeAssertions.TUnit
```

The assertions auto-import via `TUnit.Assertions.Extensions`; no extra `using` directive is needed if your project already uses TUnit. Add `using Microsoft.Extensions.Time.Testing;` to construct `FakeTimeProvider` instances in tests.

## Examples by use case

### Verify time advancement after `Advance` / `SetUtcNow`

```csharp
var fakeTime = new FakeTimeProvider();
fakeTime.Advance(TimeSpan.FromHours(2));

await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromHours(2));
```

When production code does its own internal `Advance` calls and you want tolerance:

```csharp
await Assert.That(fakeTime).HasAdvancedBy(
    total: TimeSpan.FromMinutes(30),
    tolerance: TimeSpan.FromSeconds(1));
```

For tests that snap to a specific moment:

```csharp
fakeTime.SetUtcNow(new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero));
await Assert.That(fakeTime).HasUtcNow(new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero));
```

### Recency / past / future against a fake clock

```csharp
// "Last processed within the last second of fake-clock time"
await Assert.That(service.LastProcessedAt).IsRecent(TimeSpan.FromSeconds(1), fakeTime);

// "Next-run timestamp is in the future relative to fake-clock now"
await Assert.That(service.NextRunAt).IsAfterNow(fakeTime);

// "Expiration timestamp has already passed"
await Assert.That(record.ExpiresAt).IsBeforeNow(fakeTime);
```

`IsRecent`'s `TimeProvider` parameter is optional — when omitted, `TimeProvider.System` is used:

```csharp
// System clock; useful for end-to-end tests that don't use FakeTimeProvider
await Assert.That(DateTimeOffset.UtcNow).IsRecent(TimeSpan.FromSeconds(5));
```

### Cross-cutting timing budget — compose with behavioural assertions

```csharp
// Canonical pattern: .And.WithinTimeBudget(...) after any behavioural assertion
await Assert.That(asyncOp)
    .IsEqualTo(expectedResult)
    .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));

// Composes with sibling-family chains (LogAssertions, HttpAssertions, ...)
await Assert.That(collector)
    .HasLoggedOnce()
    .AtLevel(LogLevel.Error)
    .And.WithinTimeBudget(TimeSpan.FromSeconds(2));
```

## Real-world test pattern

A complete production-code + test pair showing how `TimeProvider` injection, `FakeTimeProvider`, and these assertions compose. The production code never reads the system clock directly — every time-dependent decision goes through the injected `TimeProvider`. Tests inject `FakeTimeProvider`, drive time deterministically, and assert against fake-clock state.

```csharp
// === Production code ===
public sealed class ExpirationService
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    public DateTimeOffset? LastProcessedAt { get; private set; }
    public DateTimeOffset NextRunAt { get; private set; }

    public ExpirationService(TimeProvider? timeProvider = null, TimeSpan? ttl = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(30);
        NextRunAt = _timeProvider.GetUtcNow() + _ttl;
    }

    public Task ProcessAsync(CancellationToken ct)
    {
        LastProcessedAt = _timeProvider.GetUtcNow();
        NextRunAt = LastProcessedAt.Value + _ttl;
        return Task.CompletedTask;
    }
}

// === Test ===
[Test]
public async Task PreReleaseExpiration_advances_state_after_clock_moves_forward(CancellationToken ct)
{
    var startedAt = new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero);
    var fakeTime = new FakeTimeProvider();
    fakeTime.SetUtcNow(startedAt);

    var service = new ExpirationService(fakeTime, ttl: TimeSpan.FromMinutes(30));

    fakeTime.Advance(TimeSpan.FromMinutes(31));
    await service.ProcessAsync(ct);

    using (Assert.Multiple())
    {
        // FakeTimeProvider state — sanity check that test setup landed
        await Assert.That(fakeTime).HasUtcNow(startedAt.AddMinutes(31));

        // Production-code state vs the fake clock
        await Assert.That(service.LastProcessedAt!.Value).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
        await Assert.That(service.NextRunAt).IsAfterNow(fakeTime);
    }
}
```

What this pattern buys you:

- **Deterministic timing.** No `Thread.Sleep`, no flaky CI from real-clock drift. The test runs in milliseconds even though it simulates 31 minutes of elapsed time.
- **Both sides assertable.** `HasUtcNow` / `HasAdvanced*` confirm the *fake clock's* state; `IsRecent` / `IsBeforeNow` / `IsAfterNow` confirm *production state* relative to that fake clock.
- **No system-clock leakage.** Because production code accepts `TimeProvider` and the test injects `FakeTimeProvider`, there's no path where `DateTimeOffset.UtcNow` could sneak in.

## What this package does NOT do

- **No retry / polling.** `.Eventually()` is planned for 0.3.0; today, use the assertion's domain-specific timeout API (e.g. `LogAssertions.WithinTimeout()` for log polling).
- **No `Stopwatch.GetTimestamp()` monotonic timing** for `WithinTimeBudget`. Uses TUnit's `EvaluationMetadata<T>.Duration` which is `DateTimeOffset.Now`-based. System-clock jumps during a test method are vanishingly rare. A `.WithinTimeBudgetMonotonic()` variant is a 0.2.0 candidate if benchmark-class precision is needed.
- **No `.Elapsed(out TimeSpan)`** — see [Deferred from 0.1.0](#deferred-from-010) for why.
- **No `FakeTimeProvider.ActiveTimers` checks.** That property isn't part of the public `Microsoft.Extensions.Time.Testing` API surface; can't be observed without reflection. If Microsoft exposes it later, we add the assertion.

## Design notes

### Why `WithinTimeBudget` (not `Within`)

TUnit core already uses `.Within(...)` on tolerance assertions (`TimeSpanEqualsAssertion.Within(TimeSpan tolerance)`, `IntEqualsAssertion.Within(int days)`, etc.). Reusing `.Within` for timing budgets would collide with the existing tolerance API and confuse overload resolution. `.WithinTimeBudget` reads naturally with `.And` ("and within time budget of 500ms") and is unambiguous.

### Direct-on-source vs `.And.WithinTimeBudget` (type inference note)

`.WithinTimeBudget()` is generic in the source's value type. Two patterns work; the first infers types automatically, the second requires an explicit type argument:

```csharp
// ✅ Canonical — infers cleanly via .And continuation
await Assert.That(asyncTask).IsEqualTo(42).And.WithinTimeBudget(TimeSpan.FromSeconds(5));

// ⚠️ Direct-on-source — requires explicit type argument
await Assert.That(asyncTask).WithinTimeBudget<int>(TimeSpan.FromSeconds(5));
```

Use `.And.WithinTimeBudget` whenever you have a behavioural assertion to chain after; the explicit-type-argument form is a fallback for source-only timing.

### Why `IsBeforeNow` / `IsAfterNow` (not `IsInPast` / `IsInFuture`)

TUnit core ships `DateTimeOffset.IsInPast()` / `IsInFuture()` against `DateTimeOffset.Now` (system clock, no `TimeProvider`). Our `IsBeforeNow(TimeProvider)` / `IsAfterNow(TimeProvider)` add the `TimeProvider`-aware variants — distinct names so the reader sees at a glance which mechanism the test relies on. For system-clock tests, prefer TUnit's existing methods; for `FakeTimeProvider`-driven tests, use ours.

## Deferred from 0.1.0

### `.Elapsed(out TimeSpan)`

The original plan called for an `out` parameter to capture the elapsed time of an assertion chain. Unimplementable — `out` parameters are assigned synchronously before any await, but the wall-clock duration isn't known until the evaluator runs. Capturing post-await elapsed via `out` would write to a state-machine slot that's no longer alive.

Alternatives under consideration for 0.2.0:

- **Property-capture:** `var capture = new ElapsedCapture(); await ... .CaptureElapsed(capture); var latency = capture.Value;`
- **Tuple-return:** `var (response, latency) = await Assert.That(response).IsOk().And.WithElapsed();`
- **Callback:** `.CaptureElapsed(t => latency = t)`

Pending design call.

## Quality bar

- AOT-compatible (`IsAotCompatible=true`), trimmable (`IsTrimmable=true`)
- Net 10, C# 14, `Nullable=enable`, `TreatWarningsAsErrors=true`
- 5 Roslyn analyzer packs at full strength
- 90% line / 80% branch coverage CI gates
- Trusted Publishing (OIDC) to nuget.org
- Source Link, SBOM, deterministic builds
- MIT license throughout (`Microsoft.Extensions.TimeProvider.Testing` is MIT)

## Family

Part of an assertion family for TUnit:

- [LogAssertions.TUnit](https://github.com/JohnVerheij/LogAssertions.TUnit)
- [SnapshotAssertions.TUnit](https://github.com/JohnVerheij/SnapshotAssertions.TUnit)

The family composes — `.And.WithinTimeBudget(...)` chains after any behavioural assertion from any of these.

## License

[MIT](LICENSE)
