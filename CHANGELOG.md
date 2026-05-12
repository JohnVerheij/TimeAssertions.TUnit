# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-05-12: Failure-message enrichment, cold-start cookbook, family lockstep

Demand-driven minor. Surfaces the grep-friendly elapsed / budget / overrun tuple in every `WithinTimeBudget` failure message, adds a cookbook section for the first-fixture cold-start tax, documents the transitional `Advance` + real-time-yield shape used until `Eventually(...)` ships, and brings the repo into lockstep with the SnapshotAssertions 0.3.0 family-wide hygiene baseline.

### Added (TimeAssertions, framework-agnostic core)

- **`TimeRenderingHelpers.FormatBudgetOverrun`** now appends a grep-friendly uniform-millisecond suffix to its rendered output: `(elapsed=Xms, budget=Yms, overrun=Zms)`. Surfaces in every `WithinTimeBudget` / `WithinTimeBudgetCapturing` failure message and lets CI log scrapers and triage tooling extract the three numbers without parsing the human-readable prose. Behaviour-only change to the existing public method; signature unchanged.

### Documentation

- **New cookbook section "Accommodating first-fixture cold-start"** in `README.md`. Documents the JIT + DI container build + hosted-service startup tax that pads the first invocation in a freshly-created TUnit fixture and offers two patterns: budget-with-margin (simple) and warm-up-call (precise). Pairs the warm-up pattern with `WithinTimeBudgetCapturing` for steady-state observability.
- **New "Transitional shape: 50ms real-time yield after `Advance`"** subsection in "Limitations and future work". Documents the consumer pattern for crossing async-state-machine boundaries until the deferred `Eventually(timeout, polling)` primitive ships.
- **"Failure diagnostics" subsection** cross-references the grep-friendly suffix and the capturing variant.
- **"Entry points" table** reworded for `IsRecent` to make the null / omitted `TimeProvider` fallback explicit (`TimeProvider.System` for end-to-end tests not running under a fake clock).
- **Packaged README (`src/TimeAssertions.TUnit/README.md`)** now ships a `## Family` section listing the three siblings and a one-line note on the grep-friendly suffix in failure messages. Required TUnit version bumped to `1.44.0`.
- **`CONVENTIONS.md` upgraded to v0.3** with the `SnapshotAssertions.Render` namespace reservation for sibling-package text renderers.

### Changed

- **Dependency refresh.** Family-lockstep versions:
  - `TUnit` / `TUnit.Assertions` / `TUnit.Core`: 1.43.11 -> 1.44.0
  - `Microsoft.CodeAnalysis.BannedApiAnalyzers`: 3.3.4 -> 4.14.0
  - `Meziantou.Analyzer`: 3.0.72 -> 3.0.78
  - `SnapshotAssertions.TUnit`: 0.2.0 -> 0.3.0
- **`Directory.Build.props` sets `MeziantouAnalysisMode=all-warnings` for `src/` projects** via the path-normalised `Replace('\','/').Contains('/tests/')` predicate. Test projects retain Meziantou defaults. Production-code fixes surfaced: `TimeRenderingHelpers.FormatDuration` minute math now uses `duration.Ticks / TimeSpan.TicksPerMinute` instead of an explicit `(long)` cast on `TotalMinutes`; the four assertion-class `string.Create`-with-only-string-arguments call sites simplified to plain `$"..."` interpolation. `<NoWarn>` extended with `MA0038;MA0137;MA0174;MA0190` for the family-convention exceptions documented inline in `Directory.Build.props`.
- **`BannedSymbols.txt`** collapsed bare `#` comment lines into adjacent text-bearing lines so the file parses cleanly under the stricter BannedApiAnalyzers 4.x grammar.

### Quality

- ApiCompat strict-mode baseline bumped 0.1.0 -> 0.2.0. Behaviour-only release on the public signature surface; `CompatibilitySuppressions.xml` regenerated empty.
- AOT-publish smoke gate via `tests/TimeAssertions.TUnit.SmokeTest/` validates `dotnet publish -r linux-x64 -p:PublishAot=true` consumer-side AOT correctness on every release; SmokeTest pin bumped to `TUnit 1.44.0` and the floating `TimeAssertions.TUnit` reference to `0.3.0-*`.
- Coverage holds at the 90% line / 90% branch CI gates. Test count rises from 51 to 59: three new `FormatBudgetOverrun` unit tests plus one parameterised case covering five (elapsed, budget) pairs (the arithmetic invariant) plus one end-to-end assertion-message integration test on the adapter.

### Notes

- The deferred `Eventually(timeout, polling)` primitive moves to a post-v0.3.0 release. The 50ms-real-time-yield transitional shape is now documented in the README for consumers who hit the async-state-machine-boundary use case before the API lands.
- `HasActiveTimers` remains gated on [dotnet/extensions#7515](https://github.com/dotnet/extensions/issues/7515); no upstream movement since filing 2026-05-07.

## [0.2.0]: Naming symmetry, elapsed capture, dependency refresh

Feature release plus rolled-in housekeeping. Lockstep version bump for both packages; ApiCompat baseline pinned to 0.1.0 (the previous shipped release). The intermediate v0.1.1 housekeeping work is folded into this release rather than shipping as a separate intermediate version.

### Added

- **`HasAdvancedExactly` / `HasAdvancedApproximately`** on `FakeTimeProvider`. Renamed from `HasAdvanced` / `HasAdvancedBy` for symmetry with the rest of the family ("Exactly" vs "Approximately" makes the bounds intent explicit). The original names remain as `[Obsolete]` aliases through v0.3.x and will be removed in v0.4.0.
- **`WithinTimeBudgetCapturing(TimeSpan, Action<TimeSpan>)`**: capturing variant of `WithinTimeBudget`. Same wall-clock budget behaviour, plus an `Action<TimeSpan>` callback that always receives the measured elapsed (whether the budget was met, exceeded, or the source threw). Useful for tests that need to surface the observed timing in their failure diagnostic before the budget-overrun assertion exception propagates.

### Added (CI / process)

- **External-consumer smoke-test project** (`tests/TimeAssertions.TUnit.SmokeTest/`): references `TimeAssertions.TUnit` ONLY via `PackageReference` from a deliberately-different namespace and consumes the just-packed nupkg via a local NuGet feed at `./artifacts`. Lives outside the main `TimeAssertions.TUnit.slnx` so the unpublished local-feed version doesn't break `dotnet restore` on the main solution; CI packs the package first, then restores the smoke-test against the local feed and runs it. AOT-published with `PublishAot=true --runtime linux-x64 --self-contained` as a hard gate against future reflection / DynamicCode regressions.
- **Recursive public-API self-test project** (`tests/TimeAssertions.TUnit.SnapshotTests/`): pins the public surface using `SnapshotAssertions.TUnit.MatchesSnapshot()` against `PublicApiGenerator` output. Dogfooding for the family: no `Verify` dependency.

### Notes

- **`FakeTimeProvider.ActiveTimers` upstream proposal.** Filed as [dotnet/extensions#7515](https://github.com/dotnet/extensions/issues/7515) for the `HasActiveTimers` deferred item. `Microsoft.Extensions.Time.Testing` does not expose `ActiveTimers` publicly; if the proposal lands the assertion ships in a follow-up release.

### Deprecated

- **`HasAdvanced` and `HasAdvancedBy` carry `[Obsolete(error: false)]`.** Two-minor cycle: aliases live through v0.3.x; the v0.4.0 release removes them. Migrate via search-and-replace by name across the test suite.

### Changed

- **Dependency refresh.** Bumped to latest stable for every direct and analyzer dependency:
  - `TUnit` / `TUnit.Assertions` / `TUnit.Core`: 1.43.2 → 1.43.11
  - `Microsoft.Extensions.TimeProvider.Testing`: 9.5.0 → 10.5.0
  - `Microsoft.Sbom.Targets`: 3.0.1 → 4.1.5
  - `Microsoft.SourceLink.GitHub`: 8.0.0 → 10.0.203
  - `DotNetProjectFile.Analyzers`: 1.12.2 → 1.13.1
  - `Meziantou.Analyzer`: 2.0.219 → 3.0.72
  - `Microsoft.VisualStudio.Threading.Analyzers`: 17.13.61 → 17.14.15
  - `Roslynator.Analyzers`: 4.13.1 → 4.15.0
  - `SonarAnalyzer.CSharp`: 10.24.0.138807 → 10.25.0.139117

### Documentation

- **`CONVENTIONS.md` upgraded to v0.2.** Codifies the family-wide conventions shared across `TimeAssertions.TUnit`, `LogAssertions.TUnit`, and `SnapshotAssertions.TUnit`: trailing `CancellationToken ct = default` on every new async API, `Task.Delay(TimeSpan, TimeProvider, ct)` for polling loops, the 100/200/400/800/1000ms exponential schedule for time-based polls, the `# <Package> snapshot v<N>` header convention for `ToSnapshotString()` (TimeAssertions has no rendering of this kind today; the convention applies if/when one is added), TFM policy (LTS-anchored; multi-target during STS support windows), and the explicit "Verify is not promoted by this family: `MatchesSnapshot()` is the canonical example" stance.

### Quality numbers

- Coverage on the main suite: **98.39% line / 93.75% branch** (above the CI hard gates of 90% / 90%).
- ApiCompat strict-mode validation against the v0.1.0 baseline (`PackageValidationBaselineVersion=0.1.0`); auto-generated `CompatibilitySuppressions.xml` documents every additive change plus the two `[Obsolete]` rename markers (`HasAdvanced`, `HasAdvancedBy`).

## [0.1.0]: Initial release: TUnit-side assertions for TimeProvider-based testable time

First public release. **Positioned as the TUnit assertion package for projects committed to
`TimeProvider`-based testable time.** Two packages ship in lockstep: `TimeAssertions`
(framework-agnostic core, BCL-only) and `TimeAssertions.TUnit` (TUnit adapter, transitively
ships `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider`).

Net 10, AOT-compatible, trimmable, no runtime reflection.

### Added (TimeAssertions, framework-agnostic core)

- **`TimeRenderingHelpers`**: formatting utilities for elapsed durations and budgets in
  failure-message context. Pure, allocation-conscious.

### Added (TimeAssertions.TUnit, TUnit adapter)

`FakeTimeProvider` state assertions: the headline integration with the testable-time pattern:

- **`HasAdvanced(TimeSpan total)`**: asserts that the fake provider's current time
  differs from its construction-time start by exactly `total`. Sanity check for
  `Advance` / `SetUtcNow` calls in test setup.
- **`HasAdvancedBy(TimeSpan total, TimeSpan tolerance)`**: same with absolute tolerance.
  Useful when production code performs additional internal `Advance` calls.
- **`HasUtcNow(DateTimeOffset expected)`**: asserts that `fakeTime.GetUtcNow()` equals
  the expected moment exactly.
- **`HasUtcNowApproximately(DateTimeOffset expected, TimeSpan tolerance)`**: same with
  absolute tolerance. Useful when the expected moment is computed from integer-truncated
  minute math or chained `Advance` calls with rounding rather than a literal.

`TimeProvider`-aware `DateTimeOffset` assertions: distinct from TUnit core's
`IsInPast()` / `IsInFuture()` (which always use the system clock):

- **`IsRecent(TimeSpan window, TimeProvider? timeProvider = null)`**: asserts that the
  timestamp is within the last `window` relative to the supplied `TimeProvider`'s notion
  of "now". Defaults to `TimeProvider.System` when omitted.
- **`IsBeforeNow(TimeProvider timeProvider)`**: strict-before-now check against the
  supplied time provider.
- **`IsAfterNow(TimeProvider timeProvider)`**: strict-after-now check.

Cross-cutting timing budget: composes with any behavioural assertion via `.And`:

- **`WithinTimeBudgetAssertion<T>`**: TUnit chain extension generating the
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
- Trusted Publishing (OIDC) to nuget.org: no long-lived secrets.
- Source Link, SBOM via `Microsoft.Sbom.Targets`, deterministic builds, lock files,
  `--locked-mode` restore on CI.
- TUnit dependency pinned to **1.43.11**; `Microsoft.Extensions.TimeProvider.Testing` to
  **9.5.0**.
- License: MIT throughout (TUnit, Microsoft.Extensions.TimeProvider.Testing all MIT).

### Deferred to follow-up releases

- **`.Elapsed(...)`**: needs design call (callback vs property-capture vs tuple-return).
- **`.Eventually()`** retry/polling terminator: planned for 0.3.0.
- **`Stopwatch.GetTimestamp()`-based monotonic-clock variant** of `WithinTimeBudget`:
  candidate for 0.2.0 if benchmark-class precision is needed.
- **External-consumer smoke test + AOT-publish CI gate**: planned for 0.2.0.
- **Recursive public-API self-test** via `SnapshotAssertions.TUnit`: planned for 0.1.1.
