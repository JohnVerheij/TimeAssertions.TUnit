# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] — Initial release: TUnit-side assertions for TimeProvider-based testable time

First public release. **Positioned as the TUnit assertion package for projects committed to
`TimeProvider`-based testable time.** Two packages ship in lockstep: `TimeAssertions`
(framework-agnostic core, BCL-only) and `TimeAssertions.TUnit` (TUnit adapter, transitively
ships `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider`).

Net 10, AOT-compatible, trimmable, no runtime reflection.

### Added (TimeAssertions, framework-agnostic core)

- **`TimeRenderingHelpers`** — formatting utilities for elapsed durations and budgets in
  failure-message context. Pure, allocation-conscious.

### Added (TimeAssertions.TUnit, TUnit adapter)

`FakeTimeProvider` state assertions — the headline integration with the testable-time pattern:

- **`HasAdvanced(TimeSpan total)`** — asserts that the fake provider's current time
  differs from its construction-time start by exactly `total`. Sanity check for
  `Advance` / `SetUtcNow` calls in test setup.
- **`HasAdvancedBy(TimeSpan total, TimeSpan tolerance)`** — same with absolute tolerance.
  Useful when production code performs additional internal `Advance` calls.
- **`HasUtcNow(DateTimeOffset expected)`** — asserts that `fakeTime.GetUtcNow()` equals
  the expected moment exactly.
- **`HasUtcNowApproximately(DateTimeOffset expected, TimeSpan tolerance)`** — same with
  absolute tolerance. Useful when the expected moment is computed from integer-truncated
  minute math or chained `Advance` calls with rounding rather than a literal.

`TimeProvider`-aware `DateTimeOffset` assertions — distinct from TUnit core's
`IsInPast()` / `IsInFuture()` (which always use the system clock):

- **`IsRecent(TimeSpan window, TimeProvider? timeProvider = null)`** — asserts that the
  timestamp is within the last `window` relative to the supplied `TimeProvider`'s notion
  of "now". Defaults to `TimeProvider.System` when omitted.
- **`IsBeforeNow(TimeProvider timeProvider)`** — strict-before-now check against the
  supplied time provider.
- **`IsAfterNow(TimeProvider timeProvider)`** — strict-after-now check.

Cross-cutting timing budget — composes with any behavioural assertion via `.And`:

- **`WithinTimeBudgetAssertion<T>`** — TUnit chain extension generating the
  `WithinTimeBudget(TimeSpan)` assertion. The wall-clock duration captured by TUnit's
  `EvaluationMetadata<T>.Duration` is compared against the budget; assertion fails if
  exceeded.

  ```csharp
  await Assert.That(asyncOp)
      .IsEqualTo(expectedResult)
      .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
  ```

### Canonical chain pattern (locked in 0.1.0)

```csharp
await Assert.That(asyncOp)
    .IsEqualTo(42)
    .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
```

The `.And` continuation returns `IAssertionSource<T>`, on which the source generator's
emitted `WithinTimeBudget<T>` extension binds with full type inference. Direct-on-source
form (`Assert.That(asyncTask).WithinTimeBudget<int>(...)`) requires an explicit type
argument; `.And.WithinTimeBudget(...)` is preferred.

### Design decisions locked in 0.1.0

- **Method named `WithinTimeBudget`, not `Within`.** TUnit core already uses
  `.Within(TimeSpan)` for tolerance comparisons. `.WithinTimeBudget` reads naturally with
  `.And`, doesn't collide with the existing tolerance API, and is unambiguous about
  timing-budget intent.
- **`.WithinTimeBudget()` is post-facto, NOT cancellation.** It measures wall-clock
  duration around the assertion's evaluation and fails if the budget is exceeded; it does
  NOT abort the assertion mid-flight. Composes correctly with sibling-package timeout APIs
  (each handles its own cancellation semantics for polling / streaming workloads).
- **`IsBeforeNow` / `IsAfterNow`, not `IsInPast` / `IsInFuture`.** Distinct names from
  TUnit core's existing `IsInPast` / `IsInFuture` (which use the system clock with no
  `TimeProvider`). The article-different naming signals "this is the TimeProvider-aware
  variant" at the call site.
- **`Microsoft.Extensions.TimeProvider.Testing` is propagated, not `PrivateAssets="all"`.**
  Consumers writing `Assert.That(fakeTime).HasUtcNow(...)` need `FakeTimeProvider` itself
  in scope; making the dep transitive avoids an extra explicit reference in every test
  project that consumes us.
- **`HasActiveTimers` not shipped.** `FakeTimeProvider.ActiveTimers` isn't part of the
  public `Microsoft.Extensions.Time.Testing` surface; can't be observed without
  reflection. If Microsoft exposes it later, we add the assertion in a follow-up.
- **No `.Elapsed(out TimeSpan)`** in 0.1.0. `out` parameters are set synchronously, before
  the await; the post-await elapsed value can't be captured through them. A
  callback-based or property-capture alternative will land in 0.2.0 after the design is
  settled.

### Quality bar

- AOT-compatible (`IsAotCompatible=true`), trimmable (`IsTrimmable=true`), no runtime
  reflection in the assertion path.
- C# 14, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`.
- 5 Roslyn analyzer packs at full strength (Meziantou, SonarAnalyzer, Roslynator, VSTHRD,
  dpfa).
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` enforces no-reflection at build time.
- ApiCompat strict mode wired (`PackageValidationBaselineVersion` will pin to 0.1.0 in
  0.1.1).
- 90% line / 80% branch coverage CI gates (achieved 100% line / 100% branch as shipped).
- Trusted Publishing (OIDC) to nuget.org — no long-lived secrets.
- Source Link, SBOM via `Microsoft.Sbom.Targets`, deterministic builds, lock files,
  `--locked-mode` restore on CI.
- TUnit dependency pinned to **1.43.11**; `Microsoft.Extensions.TimeProvider.Testing` to
  **9.5.0**.
- License: MIT throughout (TUnit, Microsoft.Extensions.TimeProvider.Testing all MIT).

### Deferred to follow-up releases

- **`.Elapsed(...)`** — needs design call (callback vs property-capture vs tuple-return).
- **`.Eventually()`** retry/polling terminator — planned for 0.3.0.
- **`Stopwatch.GetTimestamp()`-based monotonic-clock variant** of `WithinTimeBudget` —
  candidate for 0.2.0 if benchmark-class precision is needed.
- **External-consumer smoke test + AOT-publish CI gate** — planned for 0.2.0.
- **Recursive public-API self-test** via `SnapshotAssertions.TUnit` — planned for 0.1.1.
