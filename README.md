# TimeAssertions.TUnit

[![CI](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/gh/JohnVerheij/TimeAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/TimeAssertions.TUnit)
[![NuGet](https://img.shields.io/nuget/v/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

A TUnit-native fluent time-assertion DSL on top of `Microsoft.Extensions.Time.Testing.FakeTimeProvider`. Built using TUnit's `[AssertionExtension]` source generator, so the assertion entry points integrate directly into TUnit's `Assert.That(...)` pipeline. Adds `TimeProvider`-aware `DateTimeOffset` checks plus a cross-cutting `.And.WithinTimeBudget(TimeSpan)` chain extension that composes with any behavioural assertion.

> **Scope:** Test projects only. Not intended for production code.

---

## Table of contents

- [Why this package](#why-this-package)
- [Install](#install)
- [Package layout](#package-layout)
- [Namespaces (and a `GlobalUsings.cs` recommendation)](#namespaces-and-a-globalusingscs-recommendation)
- [Quick start](#quick-start)
- [Why TimeProvider in tests](#why-timeprovider-in-tests)
- [Entry points](#entry-points)
  - [`FakeTimeProvider` state assertions](#faketimeprovider-state-assertions)
  - [`TimeProvider`-aware `DateTimeOffset` assertions](#timeprovider-aware-datetimeoffset-assertions)
  - [Cross-cutting timing budget](#cross-cutting-timing-budget)
- [Failure diagnostics](#failure-diagnostics)
- [Cookbook — common patterns](#cookbook--common-patterns)
- [Modern .NET 10+ practices on display](#modern-net-10-practices-on-display)
- [Design notes](#design-notes)
- [Stability intent (pre-1.0)](#stability-intent-pre-10)
- [Limitations and future work](#limitations-and-future-work)
- [Pair with](#pair-with)
- [Contributing](#contributing)
- [License](#license)

---

## Why this package

Asserting on time-dependent behaviour during tests typically devolves into either:

- Manual `Assert.True(fakeTime.GetUtcNow() == expected, ...)` plumbing in every test, or
- Real-clock waits (`Thread.Sleep`, `Task.Delay`) with arbitrary tolerances that produce flaky CI when the runner is loaded.

This library replaces both with a fluent DSL on top of Microsoft's recommended `TimeProvider` testability pattern, plus an assertion-level timing-budget extension that composes with any behavioural chain.

## Install

```bash
dotnet add package TimeAssertions.TUnit
```

**Requirements:** TUnit 1.43.11 or later, .NET 10. `TimeAssertions` (the framework-agnostic core) and `Microsoft.Extensions.TimeProvider.Testing` come transitively. The package is AOT-compatible, trimmable, and uses no runtime reflection in the assertion path.

## Package layout

This repo ships **two** NuGet packages:

| Package | Purpose | Depends on |
|---|---|---|
| [`TimeAssertions`](https://www.nuget.org/packages/TimeAssertions/) | Framework-agnostic core: `TimeRenderingHelpers` for elapsed-duration / budget-overrun formatting | BCL only |
| [`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/) | TUnit-specific entry points: `HasAdvanced()`, `HasUtcNow()`, `IsRecent()`, `IsBeforeNow()`, `IsAfterNow()`, `WithinTimeBudget()` and shorthand variants | `TimeAssertions` + `TUnit.Assertions` + `TUnit.Core` + `Microsoft.Extensions.TimeProvider.Testing` |

You install `TimeAssertions.TUnit`; `TimeAssertions` and `Microsoft.Extensions.TimeProvider.Testing` come transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are *not* shipped today — they would reuse the `TimeAssertions` core. Open a feature request if you need one.

## Namespaces (and a `GlobalUsings.cs` recommendation)

The two packages place types in two namespaces with deliberately-different scopes:

| Type / member | Namespace | Auto-imported? |
|---|---|---|
| `HasAdvanced()`, `HasUtcNow()`, `IsRecent()`, `IsBeforeNow()`, `IsAfterNow()`, `WithinTimeBudget()` (source-generated entries) | `TUnit.Assertions.Extensions` | **Yes** — TUnit auto-imports |
| `FakeTimeProvider` (the testable-clock type) | `Microsoft.Extensions.Time.Testing` | **No** — needed at the call site; recommended for `GlobalUsings.cs` |
| `TimeRenderingHelpers` (formatting utilities for failure messages) | `TimeAssertions` | **No** — needed at the call site; recommended for `GlobalUsings.cs` |
| `WithinTimeBudgetAssertion<T>` (the assertion class behind `WithinTimeBudget`) | `TimeAssertions.TUnit` | **No** — needed at the call site; recommended for `GlobalUsings.cs` |

**Recommended:** put the three non-auto-imported namespaces into a single `GlobalUsings.cs` in your test project so every test file sees them without ceremony:

```csharp
// tests/MyApp.Tests/GlobalUsings.cs
global using Microsoft.Extensions.Time.Testing;
global using TimeAssertions;
global using TimeAssertions.TUnit;
```

## Quick start

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

---

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

---

## Entry points

Three groups of entry points cover three distinct testing concerns: fake-clock state, `TimeProvider`-aware `DateTimeOffset` checks, and assertion-level timing budgets.

### `FakeTimeProvider` state assertions

| Entry point | Behaviour |
|---|---|
| `HasAdvanced(TimeSpan total)` | Asserts `fakeTime.GetUtcNow() - construction-time` equals `total` exactly. Sanity check for `Advance` / `SetUtcNow` calls in test setup. |
| `HasAdvancedBy(TimeSpan total, TimeSpan tolerance)` | Same, with absolute tolerance. Useful when production code performs additional internal `Advance` calls. |
| `HasUtcNow(DateTimeOffset expected)` | Asserts `fakeTime.GetUtcNow()` equals `expected` exactly. |
| `HasUtcNowApproximately(DateTimeOffset expected, TimeSpan tolerance)` | Same, with absolute tolerance. Useful when the expected moment is computed from integer-truncated minute math or chained `Advance` calls with rounding rather than a literal. |

```csharp
var fakeTime = new FakeTimeProvider();
fakeTime.Advance(TimeSpan.FromHours(2));

await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromHours(2));
```

### `TimeProvider`-aware `DateTimeOffset` assertions

Distinct from TUnit core's `IsInPast()` / `IsInFuture()` (which always use the system clock):

| Entry point | Behaviour |
|---|---|
| `IsRecent(TimeSpan window, TimeProvider? timeProvider = null)` | Asserts the timestamp is within the last `window` relative to the supplied `TimeProvider`'s notion of "now". Defaults to `TimeProvider.System` when omitted. |
| `IsBeforeNow(TimeProvider timeProvider)` | Strict-before-now check against the supplied time provider. |
| `IsAfterNow(TimeProvider timeProvider)` | Strict-after-now check. |

```csharp
await Assert.That(service.LastProcessedAt).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
await Assert.That(record.ExpiresAt).IsBeforeNow(fakeTime);
await Assert.That(service.NextRunAt).IsAfterNow(fakeTime);
```

### Cross-cutting timing budget

`.And.WithinTimeBudget(TimeSpan)` composes with **any** behavioural assertion. The wall-clock duration captured by TUnit's `EvaluationMetadata<T>.Duration` is compared against the budget; the chain fails if exceeded.

```csharp
// Canonical pattern: .And.WithinTimeBudget(...) after any behavioural assertion
await Assert.That(asyncOp)
    .IsEqualTo(expectedResult)
    .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));

// Composes with sibling-family chains (LogAssertions, SnapshotAssertions, ...)
await Assert.That(collector)
    .HasLoggedOnce()
    .AtLevel(LogLevel.Error)
    .And.WithinTimeBudget(TimeSpan.FromSeconds(2));
```

`.And.WithinTimeBudget()` is **post-facto**, not cancellation. The wall-clock duration is captured around the assertion's evaluation; the chain fails if the budget is exceeded but does NOT abort the assertion mid-flight. For polling / streaming workloads, use the relevant sibling package's domain-specific timeout API.

---

## Failure diagnostics

Failures render the actual measurement against the expected value, with no extra `Console.WriteLine` calls needed.

**`HasAdvanced` mismatch:**

```
Expected:
  fakeTime to have advanced 31m

Actual:
  advanced 30m (differs by 1m)
```

**`WithinTimeBudget` budget exceeded (assertion behavioural check passed but slow):**

```
Expected:
  to be equal to 42
  and completion within timing budget of 500ms

Actual:
  Value: 42 (matches)
  Timing: completed in 1.2s — exceeded budget of 500ms by 747ms
```

**Source threw (timing surface is additive; a thrown source is the dominant failure mode):**

```
Expected:
  to be equal to 42
  and completion within timing budget of 500ms

Actual:
  Source threw InvalidOperationException: connection refused
```

---

## Cookbook — common patterns

### Production code accepts `TimeProvider`; tests inject `FakeTimeProvider`

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
        await Assert.That(fakeTime).HasUtcNow(startedAt.AddMinutes(31));
        await Assert.That(service.LastProcessedAt!.Value).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
        await Assert.That(service.NextRunAt).IsAfterNow(fakeTime);
    }
}
```

What this pattern buys you:

- **Deterministic timing.** No `Thread.Sleep`, no flaky CI from real-clock drift. The test runs in milliseconds even though it simulates 31 minutes of elapsed time.
- **Both sides assertable.** `HasUtcNow` / `HasAdvanced*` confirm the *fake clock's* state; `IsRecent` / `IsBeforeNow` / `IsAfterNow` confirm *production state* relative to that fake clock.
- **No system-clock leakage.** Because production code accepts `TimeProvider` and the test injects `FakeTimeProvider`, there's no path where `DateTimeOffset.UtcNow` could sneak in.

### System clock `IsRecent` (no TimeProvider)

`IsRecent`'s `TimeProvider` parameter is optional — when omitted, `TimeProvider.System` is used. Useful for end-to-end tests that don't run under a fake clock:

```csharp
await Assert.That(DateTimeOffset.UtcNow).IsRecent(TimeSpan.FromSeconds(5));
```

### Snap to a specific moment

```csharp
fakeTime.SetUtcNow(new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero));
await Assert.That(fakeTime).HasUtcNow(new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero));
```

### Tolerance for chained `Advance` calls

```csharp
await Assert.That(fakeTime).HasAdvancedBy(
    total: TimeSpan.FromMinutes(30),
    tolerance: TimeSpan.FromSeconds(1));
```

---

## Modern .NET 10+ practices on display

The package is a deliberate showcase of modern .NET conventions:

- **AOT-compatible** (`IsAotCompatible=true`), trimmable (`IsTrimmable=true`), no runtime reflection in the assertion path.
- **`TimeProvider`-first.** All time-dependent assertions accept an optional `TimeProvider` parameter; defaults to `TimeProvider.System` only when no fake clock is needed.
- **Source-generated assertion entries** via TUnit's `[AssertionExtension]`. No interface implementation required, no reflection at runtime.
- **`CallerArgumentExpression`** on tolerance / budget parameters surfaces the caller's expression in failure messages without manual string passing.
- **Allocation-conscious failure rendering** (`string.Create` with culture-invariant interpolation; struct-based budget overrun renderer).

## Design notes

### Why `WithinTimeBudget` (not `Within`)

TUnit core already uses `.Within(...)` on tolerance assertions (`TimeSpanEqualsAssertion.Within(TimeSpan tolerance)`, `IntEqualsAssertion.Within(int days)`, etc.). Reusing `.Within` for timing budgets would collide with the existing tolerance API and confuse overload resolution. `.WithinTimeBudget` reads naturally with `.And` ("and within time budget of 500ms") and is unambiguous about timing-budget intent.

### Direct-on-source vs `.And.WithinTimeBudget` (type inference note)

`.WithinTimeBudget()` is generic in the source's value type. Two patterns work; the first infers types automatically, the second requires an explicit type argument:

```csharp
// Canonical — infers cleanly via .And continuation
await Assert.That(asyncTask).IsEqualTo(42).And.WithinTimeBudget(TimeSpan.FromSeconds(5));

// Direct-on-source — requires explicit type argument
await Assert.That(asyncTask).WithinTimeBudget<int>(TimeSpan.FromSeconds(5));
```

Use `.And.WithinTimeBudget` whenever you have a behavioural assertion to chain after; the explicit-type-argument form is a fallback for source-only timing.

### Why `IsBeforeNow` / `IsAfterNow` (not `IsInPast` / `IsInFuture`)

TUnit core ships `DateTimeOffset.IsInPast()` / `IsInFuture()` against `DateTimeOffset.Now` (system clock, no `TimeProvider`). Our `IsBeforeNow(TimeProvider)` / `IsAfterNow(TimeProvider)` add the `TimeProvider`-aware variants — distinct names so the reader sees at a glance which mechanism the test relies on. For system-clock tests, prefer TUnit's existing methods; for `FakeTimeProvider`-driven tests, use ours.

### `Microsoft.Extensions.TimeProvider.Testing` propagated, not `PrivateAssets="all"`

Consumers writing `Assert.That(fakeTime).HasUtcNow(...)` need `FakeTimeProvider` itself in scope. Making the dependency transitive avoids an extra explicit reference in every test project that consumes us.

## Stability intent (pre-1.0)

This is a 0.x release and the public API may evolve. Specifically:

- **Additive changes** (new entry points, new shorthand wrappers, new tolerance overloads) ship in any patch / minor without breaking ApiCompat.
- **Breaking changes** to existing signatures bump the minor version (0.X.0) and are called out in the [CHANGELOG](CHANGELOG.md).
- **`PackageValidationBaselineVersion`** is pinned to the previous shipped version starting from 0.1.1, so ApiCompat breakage is caught at pack time.

The 1.0 milestone signals API stability — see [Limitations and future work](#limitations-and-future-work) for what's still being designed.

## Limitations and future work

### `.Elapsed(out TimeSpan)`

The original plan called for an `out` parameter to capture the elapsed time of an assertion chain. Unimplementable as written — `out` parameters are assigned synchronously before any await, but the wall-clock duration isn't known until the evaluator runs. Capturing post-await elapsed via `out` would write to a state-machine slot that's no longer alive.

Alternatives under consideration for 0.2.0:

- **Property-capture:** `var capture = new ElapsedCapture(); await ... .CaptureElapsed(capture); var latency = capture.Value;`
- **Tuple-return:** `var (response, latency) = await Assert.That(response).IsOk().And.WithElapsed();`
- **Callback:** `.CaptureElapsed(t => latency = t)`

Pending design call.

### Other deferred items

- **`.Eventually()` retry / polling terminator** — planned for 0.3.0.
- **`Stopwatch.GetTimestamp()`-based monotonic-clock variant** of `WithinTimeBudget` — candidate for 0.2.0 if benchmark-class precision is needed. Today, `WithinTimeBudget` uses TUnit's `EvaluationMetadata<T>.Duration` (`DateTimeOffset.Now`-based); system-clock jumps during a test method are vanishingly rare.
- **`HasActiveTimers`** — `FakeTimeProvider.ActiveTimers` isn't part of the public `Microsoft.Extensions.Time.Testing` API surface; can't be observed without reflection. If Microsoft exposes it later, we add the assertion in a follow-up.
- **External-consumer smoke test + AOT-publish CI gate** — planned for 0.2.0.

## Pair with

- **[`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/)** — fluent log assertions over `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. Use `.And.WithinTimeBudget(...)` to add a timing budget to any `HasLogged()` chain.
- **[`SnapshotAssertions.TUnit`](https://www.nuget.org/packages/SnapshotAssertions.TUnit/)** — text-snapshot assertions for API-surface tests and similar deterministic-string scenarios. Coexists with Verify; covers the 80% case without coverage friction.

## Contributing

Issues and pull requests welcome. Before opening a PR:

- Run `dotnet build` and `dotnet test` locally; the CI pipeline enforces the same quality bar (zero warnings as errors, 90% line / 80% branch coverage minimum).
- Match the existing code style (`.editorconfig` is authoritative; `dotnet format` covers formatting).
- For new assertions, include a test for both the happy path and a representative failure case so the failure-message rendering is verified.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full PR review checklist and API design principles.

## License

[MIT](LICENSE)
