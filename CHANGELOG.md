# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.7.0] - 2026-06-04: eventually + positive-count active-timer assertions

Minor release. Adds real-time "eventually" active-timer assertions for the asynchronous timer-disposal race a synchronous leak check cannot see, plus synchronous positive-count assertions to express a lower bound on the active set.

### Added

- **`Assert.That(provider).HasNoActiveTimersEventually(TimeSpan timeout, TimeSpan? pollingInterval = null, CancellationToken ct = default)`** (TUnit adapter) polls the live `ActiveTimerCount` on the real wall clock until it reaches zero, or the timeout elapses. A `BackgroundService` / `IHostedService` commonly disposes its timer on a continuation that runs after `StopAsync` returns to the caller, so a synchronous `HasNoActiveTimers()` check just after stop can still see the timer; the poll gives that continuation time to run. The condition is checked once before the first delay, so an already-clean provider passes without waiting. On timeout the failure names each surviving timer by its schedule with a grep-friendly `(count=N)` trailer.
- **`Assert.That(provider).HasActiveTimerCountEventually(int count, TimeSpan timeout, TimeSpan? pollingInterval = null, CancellationToken ct = default)`** (TUnit adapter) is the count-targeted sibling: polls until `ActiveTimerCount` equals `count`, for an active set that settles to a steady state on a background continuation. On timeout it renders the expected and actual counts with an `(expected=N, actual=M)` trailer plus the active timers' schedules.
- **`Assert.That(provider).HasActiveTimers()`** (TUnit adapter) passes when at least one timer is active: the positive-presence counterpart of `HasNoActiveTimers()` for the registration half of a leak test, without pinning the exact count. Source-generated via `[GenerateAssertion]`.
- **`Assert.That(provider).HasAtLeastActiveTimerCount(int count)`** (TUnit adapter) passes when the active count is at least `count`, for when a lower bound rather than an exact count is the natural expectation. On a shortfall it renders the required minimum and the actual count with a `(minimum=N, actual=M)` trailer plus the active timers' schedules. Source-generated via `[GenerateAssertion]`.
- **Positional-`CancellationToken` overloads** of `HasNoActiveTimersEventually` and `HasActiveTimerCountEventually` (TUnit adapter) let a token follow the timeout positionally while keeping the default poll interval, so the common case reads as `(timeout, ct)` / `(count, timeout, ct)` instead of the named `ct:` form. They forward to the canonical chain with `pollingInterval: null`; the token parameter has no default, so a bare `(timeout)` call stays unambiguous, matching TUnit's `WaitsFor` convention.
- **`TimeAssertions.TimeRenderingHelpers.FormatActiveTimerSurvivors(...)` and `FormatActiveTimerAtLeastShortfall(...)`** (framework-agnostic core) render the survivor list for the eventually-timeout messages and the at-least shortfall message, ordering survivors deterministically (by due time, then period) so the messages are snapshot-stable.

The poll uses a real `Task.Delay` loop against a wall-clock deadline rather than a fake-time advance: hosted-service timer disposal happens on a real asynchronous continuation, which a fake clock cannot drive. The default poll interval is 10 ms; supply your own via the optional `pollingInterval`. A canceled `CancellationToken` throws `OperationCanceledException` so the test is recorded as canceled rather than failed.

## [0.6.0] - 2026-06-03: active-timer leak + pending-timer due-time assertions, Renovate + supply-chain hardening

Minor release. Adds the family's first timer-leak assertions: `HasNoActiveTimers()` and `HasActiveTimerCount(int)` over a new framework-agnostic `ObservableTimeProvider` decorator, filling the gap `FakeTimeProvider` leaves open for hosted-service timer-disposal tests, plus pending-timer due-time assertions (`HasNextTimerDueApproximately()` / `HasPendingTimerDueWithin()`) that inspect a scheduled timer's due time without advancing the clock. Also folds in the dependency-automation switch to `Renovate` and the GitHub Actions supply-chain hardening that had accumulated on the unreleased line, and starts merging core + adapter coverage in CI so the gate measures the core suite directly.

### Added

- **`TimeAssertions.ObservableTimeProvider`** (framework-agnostic core) is a `TimeProvider` decorator that tracks the `ITimer` instances created against it, exposing `ActiveTimerCount` and an `ActiveTimers` snapshot. Wrap any inner provider (typically a `FakeTimeProvider`) to detect timers that a `BackgroundService` / `IHostedService` started but did not dispose. Fills the gap [dotnet/extensions#7515](https://github.com/dotnet/extensions/issues/7515) leaves open (`FakeTimeProvider` does not surface its own timers). Reflection-free, AOT-compatible, and thread-safe.
- **`Assert.That(provider).HasNoActiveTimers()`** (TUnit adapter) is the canonical timer-leak check after a hosted service stops. On failure it names each surviving timer by the schedule it carries (`[dueTime=..., period=...]`; a one-shot timer renders as `period=one-shot`) with a grep-friendly `(count=N)` trailer, instead of reporting a bare integer. Source-generated via `[GenerateAssertion]`.
- **`Assert.That(provider).HasActiveTimerCount(int)`** (TUnit adapter) asserts the exact number of active timers, for the registration half of a disposal test. On mismatch it renders the expected and actual counts plus each active timer's schedule, with an `(expected=N, actual=M)` trailer. For an asynchronous disposal race, poll the upstream primitive instead: `await Assert.That(() => provider.ActiveTimerCount).Eventually(c => c == 0, timeout)`.
- **`TimeAssertions.ActiveTimerInfo`** (framework-agnostic core) is a readonly record struct describing a tracked timer's schedule (`DueTime`, `Period`), returned by `ObservableTimeProvider.ActiveTimers`.
- **`TimeAssertions.TimeRenderingHelpers.FormatActiveTimerLeak(...)` and `FormatActiveTimerCountMismatch(...)`** (framework-agnostic core) render the two failure messages above, ordering survivors deterministically (by due time, then period) so the messages are snapshot-stable.
- **`Assert.That(provider).HasNextTimerDueApproximately(TimeSpan expected, TimeSpan tolerance)`** (TUnit adapter) asserts that the next pending timer's due time is within `tolerance` of `expected`. The "next" timer is the one with the smallest due time among the enabled (non-infinite) active timers. The schedule is read from the timer without advancing the clock. On failure it names the expected and observed due times and the delta, or notes that no enabled timer was pending, with a grep-friendly `(expected=Xms, tolerance=Yms, actual=Zms, delta=Wms)` trailer. Source-generated via `[GenerateAssertion]`.
- **`Assert.That(provider).HasPendingTimerDueWithin(TimeSpan min, TimeSpan max)`** (TUnit adapter) asserts that the next pending timer's due time falls within the inclusive range `[min, max]`. Shares the same pending-timer capability as `HasNextTimerDueApproximately`; useful when a bound rather than a point estimate is the natural expectation. On failure it renders the range and observed due time, or notes that no enabled timer was pending, with a `(min=Xms, max=Yms, actual=Zms)` trailer.
- **`TimeAssertions.ObservableTimeProvider.NextTimerDueTime`** (framework-agnostic core) is a `TimeSpan?` read-only property exposing the smallest due time among the enabled active timers, or `null` when no enabled timer is pending. Timers whose due time is `Timeout.InfiniteTimeSpan` (disabled until re-armed) are excluded. Computed under the internal lock, so it is a consistent snapshot under concurrent timer activity. Reflection-free, AOT-compatible.
- **`TimeAssertions.TimeRenderingHelpers.FormatNextTimerDueMismatch(...)` and `FormatNextTimerDueOutOfRange(...)`** (framework-agnostic core) render the two failure messages above, including the no-pending-timer case.

### Changed

- CI collects code coverage from both the adapter and the framework-agnostic core test suites and merges the two cobertura reports (`ReportGenerator`) before the threshold gate, in place of measuring the adapter suite alone. The core suite exercises core types such as `ObservableTimeProvider`'s clock delegation and timer `Change` / `DisposeAsync` that no assertion chain reaches, so merging keeps the 90% line / 90% branch gate honest as the core grows. CI-only; no effect on shipped packages.
- Removed `paths-ignore` from `.github/workflows/ci.yml` so the `Build, test & pack` required check always reports a status. Without the fix, docs-only PRs stuck in `Expected - Waiting for status to be reported` and could not satisfy branch protection. Cross-family sweep: identical fix applied to the other five family repos as part of their open `chore/infra-family-consistency-sweep` PRs (TimeAssertions has no open sweep PR, hence the dedicated PR for this repo).
- Adopted `Renovate` (`.github/renovate.json`) for dependency updates and version-literal sync in place of the prior `Dependabot` + `SyncVersionRefs` MSBuild pipeline. `Renovate` bumps `Directory.Packages.props` and the four files that carry the TUnit version literal (`README.md`, `src/TimeAssertions.TUnit/README.md`, `.github/ISSUE_TEMPLATE/bug_report.yml`, and `tests/TimeAssertions.TUnit.SmokeTest/TimeAssertions.TUnit.SmokeTest.csproj`) in a single PR via `customManagers`. Patch- and minor-bump auto-merge is preserved through Renovate's `platformAutomerge: true` once CI passes; major bumps stay manual. No effect on shipped packages.
- Extended the Renovate auto-merge `packageRule` to cover `digest`, `pin`, `pinDigest`, and `lockFileMaintenance` updateTypes alongside `minor` and `patch`. Closes a gap where SHA-pinned GitHub Actions digest bumps (Renovate's `updateType: "digest"`) would sit open with green CI but no auto-merge enabled.
- Added a Renovate `packageRule` grouping the three TUnit packages (`TUnit`, `TUnit.Assertions`, `TUnit.Core`) into a single PR per release. They share a source repo and bump in lockstep; the default Renovate behavior of one PR per package wastes CI runs and risks partially-applied bumps if one merges before the others.
- Added GitHub Actions workflow security scanning. `.github/workflows/zizmor.yml` runs `zizmor` (blocking, with findings shown as inline annotations) on every workflow change; `.github/workflows/codeql.yml` now analyzes the `actions` language alongside `csharp`; `.github/workflows/scorecard.yml` (OpenSSF Scorecard) and `.github/workflows/dependency-review.yml` (fails a PR that adds a high-severity-vulnerable dependency) are new. Added the Renovate `helpers:pinGitHubActionDigestsToSemver` preset so any newly-introduced action is auto-pinned to a commit SHA. CI-only; no effect on shipped packages.

### Removed

- `.github/workflows/sync-version-refs.yml` and the `SyncVersionRefs` MSBuild target in `Directory.Build.targets`. The template/render duplication (the `*.template.*` siblings of the four version-bearing files) is replaced by the `Renovate` `customManagers` regex described above; the rendered files are now the only files.
- `.github/dependabot.yml` and `.github/workflows/dependabot-auto-merge.yml`. `Renovate` covers the same NuGet and GitHub Actions ecosystems; running both would produce duplicate PRs.

### Fixed

- `README.md`: the table-of-contents entry for the cookbook section now points to `#cookbook-common-patterns`. GitHub's slugger drops the colon from `Cookbook: common patterns` to produce a single-hyphen anchor; the previous `#cookbook--common-patterns` was a broken link.
- `README.md`: the "Since TUnit X.Y.Z" reference in the cookbook section that documents the upstream `Eventually` / `WaitsFor` `CancellationToken` overload now correctly cites `1.45.0` (the version that shipped the feature) rather than `1.45.8` (the package's current TUnit pin at the time the section was written; the prior `SyncVersionRefs` target was substituting the current version into a historical reference).

### Security

- Closed an arbitrary-code-execution vector in the now-removed `sync-version-refs` workflow. The workflow ran under `pull_request_target` with `contents: write` and executed `dotnet restore` / `dotnet build` against the PR head, which would have allowed any non-Dependabot PR author to execute code with a write-scoped repository token (via custom MSBuild tasks, `.targets` files, or analyzers in the PR). The workflow shipped only in this unreleased line and the vulnerability does not affect any released package.
- Set `persist-credentials: false` on every `actions/checkout` (`ci.yml`, `codeql.yml`, `release.yml`) so the job's repository token is not written into `.git/config`, where an artifact upload or later step could exfiltrate it, and moved the coverage-report path in `ci.yml` from inline `${{ }}` expansion into an `env:` variable to remove a shell template-injection vector. Both were surfaced by the new `zizmor` audit; CI-only, no released package is affected.
- Tightened GitHub Actions token permissions to least privilege. The `codeql` and `release` workflows now declare their write scopes (`security-events` for code-scanning upload; `contents` / `id-token` / `packages` / `attestations` for publishing) at the job level with a read-only workflow-level default, rather than granting those scopes for the whole workflow run. No functional change; it narrows the token blast radius and satisfies the OpenSSF Scorecard Token-Permissions check.

## [0.5.0] - 2026-05-19: rate-limit assertion, OCE propagation, TUnit 1.45.8

Minor release that adds the first rate-limit assertion (`WasInvokedAtMostOncePer`) to the package, fixes a latent cancellation-handling behaviour in `WithinTimeBudget` / `WithinTimeBudgetCapturing` (external `OperationCanceledException` was wrapped as an assertion failure rather than propagated), and bumps the TUnit dependency to 1.45.8.

### Added

- **`RateLimitAssertions.WasInvokedAtMostOncePer(this IReadOnlyList<DateTimeOffset>, TimeSpan)`** asserts that consecutive timestamps in a recorded invocation log maintain at least the specified minimum interval. The first violating pair fails the assertion with a message naming the violating index, the observed gap, and the required minimum. Empty and single-element sequences pass trivially; the boundary case `gap == interval` passes. Source-generated via `[GenerateAssertion]` so the chain surface is `Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30))`.
- **`TimeAssertions.TimeRenderingHelpers.FormatRateLimitViolation(IReadOnlyList<DateTimeOffset>, int, TimeSpan, TimeSpan)`** renders the multi-line failure message for `WasInvokedAtMostOncePer` violations, with a grep-friendly fixed-unit parenthetical `(gap=Xms, minimum=Yms)` analogous to `FormatBudgetOverrun`'s `(elapsed=, budget=, overrun=)` trailer.

### Changed

- **BREAKING:** **`WithinTimeBudgetAssertion<T>` and `WithinTimeBudgetCapturingAssertion<T>`** now propagate external `OperationCanceledException` instead of wrapping it as an assertion failure. When a parent `[Timeout]` fires or the test runner cancels, the wrapped operation's `OperationCanceledException` flows through the assertion via `ExceptionDispatchInfo.Capture(...).Throw()` so the test is recorded as cancelled, not failed. The capturing variant additionally skips invoking the capture callback on cancellation: a partial elapsed from a cancelled operation would mislead consumers about the operation's real cost. Non-`OperationCanceledException` source exceptions continue to surface as `AssertionResult.Failed` exactly as before. Consumer tests that asserted `Throws<AssertionException>` against a cancelled `WithinTimeBudget` chain must update to expect `Throws<OperationCanceledException>` (or rely on the test runner's native cancellation reporting).
- **TUnit dependency bumped `1.44.0` -> `1.45.8`** (and the external-consumer smoke-test pin). The 1.45 line adds `CancellationToken` overloads to upstream `Eventually` / `WaitsFor`; the cookbook section "Waiting for an asynchronous effect after `Advance(...)`" documents the CT-bearing variant. The packed `README.md` requirement line bumps to "TUnit 1.45.8 or later" accordingly.
- `README.md`: added a fourth Entry-points subgroup "Rate-limit assertions on invocation timestamps" (with matching TOC anchor), plus updates to Package layout, Namespaces, and Install/Requirements that reference `WasInvokedAtMostOncePer()`.
- `README.md`: added the cookbook section "Verifying a periodic-probe suppression window", pairing recorded log timestamps with `WasInvokedAtMostOncePer` for the ping-escalation pattern.
- `README.md`: reframed the "Deferred items" entry for `HasActiveTimers` as "tracked upstream", explicit about the no-reflection rule and the consumer-side `ObservableTimeProvider` bridge workaround.
- `README.md`: expanded the family roster to six packages, adding `JsonAssertions.TUnit` and `SseAssertions.TUnit` to the "Family compatibility" section, the "Pair with" section, and the "shared across" line in Contributing.
- `SECURITY.md`: updated the supported-versions table to reflect 0.5.x as the current line and 0.4.x as the previous-stable line.
- `SECURITY.md`: updated the supply-chain attestation table to name the actual action versions in use today (`actions/attest-build-provenance@v4.1.0` and `actions/attest@v4.1.0`).

## [0.4.0] - 2026-05-13: TimelineRenderer, obsolete alias removal, upstream-Eventually migration cookbook

Minor release that adds the first concrete renderer under the family-shared `*.Render` namespace convention, fulfils the v0.2.0 CHANGELOG commitment to remove the renamed `HasAdvanced` / `HasAdvancedBy` aliases, and documents the canonical upstream polling pattern for consumers crossing async-state-machine boundaries after `FakeTimeProvider.Advance(...)`.

The originally-planned `Eventually(timeout, predicate)` polling primitive (deferred in v0.3.0) is **no longer scoped** for this package. Investigation during v0.4.0 implementation confirmed that `TUnit.Assertions` ships the same surface as a built-in extension method (`Assert.That(getter).Eventually(assert => assert.IsEqualTo(expected), TimeSpan.FromSeconds(5))`, alias for `WaitsFor`, available since TUnit v1.13.69 / 2026-02-14). A sibling family-side implementation would fragment the polling surface for no net consumer benefit. The transitional 50ms-real-time-yield shape documented in v0.3.0 is replaced in the cookbook by the upstream pattern.

### Added (TimeAssertions, framework-agnostic core)

- **`TimeAssertions.Render.TimelineRenderer.Render(DateTimeOffset epoch, IReadOnlyList<TimelineEvent> events)`** renders a sequence of `(Timestamp, Label)` events as deterministic multi-line text suitable for snapshot comparison. Each event renders as `+{deltaMs}ms label` (or `-{absDeltaMs}ms label` for events before the epoch); empty input renders as `string.Empty`. The renderer preserves input order verbatim, including ties on `Timestamp`: caller sorts.
- **`TimeAssertions.Render.TimelineEvent(DateTimeOffset Timestamp, string Label)`** value type (record struct) consumed by the renderer. `Label` is non-null by contract.
- Pairs naturally with `Assert.That(rendered).MatchesSnapshot()` from the sibling `SnapshotAssertions.TUnit` package. The two-line composition is deliberate: `TimeAssertions` does not take a hard dependency on `SnapshotAssertions.TUnit`, so consumers who do not snapshot are unaffected.

### Removed

- **`HasAdvanced(this FakeTimeProvider, TimeSpan)`**: the `[Obsolete]` alias carried since v0.2.0 is removed. Migrate to `HasAdvancedExactly(this FakeTimeProvider, TimeSpan)` via search-and-replace.
- **`HasAdvancedBy(this FakeTimeProvider, TimeSpan, TimeSpan)`**: the `[Obsolete]` alias carried since v0.2.0 is removed. Migrate to `HasAdvancedApproximately(this FakeTimeProvider, TimeSpan, TimeSpan)` via search-and-replace.

### Documentation

- **New cookbook section "Pinning the moment-graph of a multi-event sequence"** in `README.md`. Worked example pairing `TimelineRenderer.Render(epoch, events)` with `MatchesSnapshot()` from `SnapshotAssertions.TUnit`. Documents the two-line composition pattern and explains why no in-package chain wrapper ships in v0.4.0.
- **New cookbook section "Waiting for an asynchronous effect after `Advance(...)`"** in `README.md`. Documents the upstream `Assert.That(getter).Eventually(...)` polling pattern as the canonical replacement for the `Advance` + fixed-yield + `Assert` shape. Covers both the source-already-evaluated case (e.g. `HasLogged` predicate) and the externally-updated-counter case (e.g. `ObservableTimerCount`).
- **Removed "Transitional shape: 50ms real-time yield after `Advance`"** subsection. The transitional language is no longer accurate: the upstream alternative existed at v0.3.0 publish time; the v0.3.0 doc was written without checking what TUnit already ships.

### Changed

- **`PackageValidationBaselineVersion` bumped to `0.3.0`** in both csproj files. `CompatibilitySuppressions.xml` carries `CP0001` / `CP0002` entries for the two removed alias methods and their generator-emitted assertion classes; the `CP0003` baseline-identity entry targets `0.3.0.0`.
- **Dependency refresh.**
  - `DotNetProjectFile.Analyzers`: 1.13.1 -> 1.14.0
  - `Meziantou.Analyzer`: 3.0.78 -> 3.0.84
  - `Microsoft.Extensions.TimeProvider.Testing`: 10.5.0 -> 10.6.0 (verified against [dotnet/extensions#7515](https://github.com/dotnet/extensions/issues/7515); the `HasActiveTimers` proposal remains open, so no new API surface is unlocked by this bump)
  - `Microsoft.SourceLink.GitHub`: 10.0.203 -> 10.0.300

### Quality

- ApiCompat strict-mode reports two intentional binary-breaking removals against the v0.3.0 baseline; both are pre-announced in the v0.2.0 CHANGELOG and suppressed with `IsBaselineSuppression=true`.
- Test count nets 59 -> 60: the five obsolete-alias regression tests (`HasAdvanced_obsoleteAlias_StillWorks`, `HasAdvancedBy_obsoleteAlias_StillWorks`, `HasAdvanced_HasObsoleteAttribute`, `HasAdvancedBy_HasObsoleteAttribute`, `HasAdvancedBy_NegativeTolerance_ThrowsArgumentOutOfRange`) are removed alongside the methods they covered (-5); six new `TimelineRendererTests` cover empty / single-event / multi-event-ordering / negative-delta / duplicate-timestamp / null-events-throws contracts (+6). Coverage holds at the 90% line / 90% branch CI gates.
- Public-API snapshots regenerated: `PublicApiTests.TimeAssertionsTUnitPublicApiHasNotChangedAsync.expected.txt` to reflect the removed alias surface, and `PublicApiTests.TimeAssertionsPublicApiHasNotChangedAsync.expected.txt` to add the new `TimeAssertions.Render` namespace surface.

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

[Unreleased]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.7.0...HEAD
[0.7.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.7.0
[0.6.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.6.0
[0.5.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.5.0
[0.4.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.4.0
[0.3.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.3.0
[0.2.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.2.0
[0.1.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.1.0
