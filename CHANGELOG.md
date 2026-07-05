# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> *History note:* All version sections were reformatted on 2026-07-05 for one-time
> [CONVENTIONS &sect;CHANGELOG](CONVENTIONS.md) conformance: forbidden sub-headers folded into
> the six Keep a Changelog headers, non-user-facing content removed (internal refactors, test
> counts, coverage numbers, CI and build hygiene, governance churn, roadmap notes), bullets
> kept in past-tense active voice with code-formatted API leads. The nuget.org Release Notes
> tab and the GitHub Release for each shipped version are unchanged. A CI `changelog-lint` gate
> keeps future sections conforming; each is frozen per Rule 7 once shipped.

## [Unreleased]

## [0.9.0] - 2026-06-12: observe timer fires

Minor release. Adds fire observation to `ObservableTimeProvider`: a count of how many times timer callbacks ran, next to the existing leak and pending-due assertions. Purely additive.

### Added

- **`Assert.That(time).HasTimerFiredCount(int expected)`**, **`HasNoTimerFired()`**, and **`HasTimerFiredAtLeast(int count)`** assert how many times timer callbacks fired across every timer the provider created. `ObservableTimeProvider` now wraps each timer callback to count its fires. The count is cumulative and is not reset on disposal. With a `FakeTimeProvider`, a fire is counted each time test code advances fake time past a due or period boundary, which keeps the count deterministic. `ObservableTimeProvider.TimerFireCount` (cumulative) and `ActiveTimerInfo.TimesFired` (per timer) expose the same data on the framework-agnostic core.
- `ObservableTimeProvider.CreateTimer` now validates that the callback is non-null up front (`ArgumentNullException`), matching the rest of the surface, instead of deferring to the inner provider.

### Fixed

- Once a periodic timer fires, `NextTimerDueTime` (and the `HasNextTimerDueApproximately` / `HasPendingTimerDueWithin` assertions) now report the timer's period, since that is when the next callback is due. A fired one-shot timer is disabled and drops out of the pending-due calculation. Previously the due time stayed frozen at its creation value until an explicit `Change`.
- Corrected stale documentation that claimed the package "deliberately does not ship its own polling assertion": it has shipped `HasNoActiveTimersEventually` and its count-targeted siblings since `0.7.0`/`0.8.0`. The README and the `ActiveTimerAssertions` XML docs now point to those overloads for the asynchronous disposal race, reserving generic `Eventually` for arbitrary conditions.

## [0.8.1] - 2026-06-06: no library change, release-notes tooling only

Tooling release. No library API or behavior change.

**Upgrade (from 0.8.0):** `0.8.0` contained **source-breaking** changes that its published release notes did not surface:

- The `CancellationToken` parameter on `HasNoActiveTimersEventually` and `HasActiveTimerCountEventually` was renamed from `ct` to `cancellationToken`. A call that passed the token by name (`ct: token`) must become `cancellationToken: token`; positional calls are unaffected.
- `EventuallyTimerAssertionExtensions` moved from the `TimeAssertions.TUnit` namespace to `TUnit.Assertions.Extensions`. Code referencing the type by its full name must update the namespace; use through `Assert.That(...)` is unaffected.

See the 0.8.0 `BREAKING` section below for the full detail.

## [0.8.0] - 2026-06-05: bounded-count eventually assertions, CancellationToken naming convention

Minor release. Adds asynchronous lower-bound, upper-bound, and at-least-one active-timer assertions, completing the bounded-count set begun in `0.7.0`. Aligns the active-timer "eventually" assertions with the BCL `CancellationToken` naming and discoverability convention. Contains breaking changes (parameter rename and a namespace move).

### BREAKING

- The `CancellationToken` parameter on `HasNoActiveTimersEventually` and `HasActiveTimerCountEventually` is renamed from `ct` to `cancellationToken`, matching the BCL convention and the new bounded-count assertions. A call that passed the token by name (`ct: token`) must use `cancellationToken: token`; positional calls are unaffected.
- `EventuallyTimerAssertionExtensions` moved from the `TimeAssertions.TUnit` namespace to `TUnit.Assertions.Extensions`. Code that referenced the type by its full name must update the namespace; code that used the extension methods through `Assert.That(...)` is unaffected (and no longer needs `using TimeAssertions.TUnit;` for them).

### Added

- **`Assert.That(time).HasAtLeastActiveTimerCountEventually(count, timeout, ...)`** polls the live active-timer count until it is at least `count`. The asynchronous lower-bound sibling of the synchronous `HasAtLeastActiveTimerCount(count)` and the exact-count `HasActiveTimerCountEventually`: the right shape for an asynchronous registration wait where more than one timer may register, where the exact-count form would flake once an additional timer registers and the synchronous form would race a not-yet-registered timer.
- **`Assert.That(time).HasAtMostActiveTimerCountEventually(count, timeout, ...)`** polls until the active-timer count is at most `count`. The upper-bound sibling for "the active set settles to no more than N" without pinning the exact survivor count. `HasAtMostActiveTimerCountEventually(0, ...)` is equivalent to `HasNoActiveTimersEventually`.
- **`Assert.That(time).HasActiveTimersEventually(timeout, ...)`** polls until at least one timer is active. The asynchronous counterpart of `HasActiveTimers()` and a named shorthand for `HasAtLeastActiveTimerCountEventually(1, ...)`.
- Positional-`CancellationToken` sugar overloads for all three new assertions, matching the existing `(timeout, token)` and `(count, timeout, token)` shapes.

### Changed

- The positional-`CancellationToken` sugar (`EventuallyTimerAssertionExtensions`) now lives in the globally imported `TUnit.Assertions.Extensions` namespace, the same namespace the source generator emits the canonical extensions into, so the `(timeout, token)` / `(count, timeout, token)` forms are discoverable wherever the generated extensions are, without an extra `using TimeAssertions.TUnit;`.

## [0.7.0] - 2026-06-04: eventually and positive-count active-timer assertions

Minor release. Adds real-time "eventually" active-timer assertions for the asynchronous timer-disposal race a synchronous leak check cannot see, plus synchronous positive-count assertions to express a lower bound on the active set.

### Added

- **`Assert.That(provider).HasNoActiveTimersEventually(TimeSpan timeout, TimeSpan? pollingInterval = null, CancellationToken ct = default)`** (TUnit adapter) polls the live `ActiveTimerCount` on the real wall clock until it reaches zero, or the timeout elapses. A `BackgroundService` / `IHostedService` commonly disposes its timer on a continuation that runs after `StopAsync` returns to the caller, so a synchronous `HasNoActiveTimers()` check just after stop can still see the timer; the poll gives that continuation time to run. The condition is checked once before the first delay, so an already-clean provider passes without waiting. On timeout the failure names each surviving timer by its schedule with a grep-friendly `(count=N)` trailer.
- **`Assert.That(provider).HasActiveTimerCountEventually(int count, TimeSpan timeout, TimeSpan? pollingInterval = null, CancellationToken ct = default)`** (TUnit adapter) is the count-targeted sibling: polls until `ActiveTimerCount` equals `count`, for an active set that settles to a steady state on a background continuation. On timeout it renders the expected and actual counts with an `(expected=N, actual=M)` trailer plus the active timers' schedules.
- **`Assert.That(provider).HasActiveTimers()`** (TUnit adapter) passes when at least one timer is active: the positive-presence counterpart of `HasNoActiveTimers()` for the registration half of a leak test, without pinning the exact count. Source-generated via `[GenerateAssertion]`.
- **`Assert.That(provider).HasAtLeastActiveTimerCount(int count)`** (TUnit adapter) passes when the active count is at least `count`, for when a lower bound rather than an exact count is the natural expectation. On a shortfall it renders the required minimum and the actual count with a `(minimum=N, actual=M)` trailer plus the active timers' schedules. Source-generated via `[GenerateAssertion]`.
- **Positional-`CancellationToken` overloads** of `HasNoActiveTimersEventually` and `HasActiveTimerCountEventually` (TUnit adapter) let a token follow the timeout positionally while keeping the default poll interval, so the common case reads as `(timeout, ct)` / `(count, timeout, ct)` instead of the named `ct:` form. They forward to the canonical chain with `pollingInterval: null`; the token parameter has no default, so a bare `(timeout)` call stays unambiguous, matching TUnit's `WaitsFor` convention.
- **`TimeAssertions.TimeRenderingHelpers.FormatActiveTimerSurvivors(...)` and `FormatActiveTimerAtLeastShortfall(...)`** (framework-agnostic core) render the survivor list for the eventually-timeout messages and the at-least shortfall message, ordering survivors deterministically (by due time, then period) so the messages are snapshot-stable.

The poll uses a real `Task.Delay` loop against a wall-clock deadline rather than a fake-time advance: hosted-service timer disposal happens on a real asynchronous continuation, which a fake clock cannot drive. The default poll interval is 10 ms; supply your own via the optional `pollingInterval`. A canceled `CancellationToken` throws `OperationCanceledException` so the test is recorded as canceled rather than failed.

## [0.6.0] - 2026-06-03: active-timer leak and pending-timer due-time assertions

Minor release. Adds the family's first timer-leak assertions: `HasNoActiveTimers()` and `HasActiveTimerCount(int)` over a new framework-agnostic `ObservableTimeProvider` decorator, filling the gap `FakeTimeProvider` leaves open for hosted-service timer-disposal tests, plus pending-timer due-time assertions (`HasNextTimerDueApproximately()` / `HasPendingTimerDueWithin()`) that inspect a scheduled timer's due time without advancing the clock.

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

## [0.5.0] - 2026-05-19: rate-limit assertion, OCE propagation, TUnit 1.45.8

Minor release. Adds the first rate-limit assertion (`WasInvokedAtMostOncePer`), fixes a latent cancellation-handling behavior in `WithinTimeBudget` / `WithinTimeBudgetCapturing` (external `OperationCanceledException` was wrapped as an assertion failure rather than propagated), and bumps the TUnit dependency to 1.45.8.

### Added

- **`RateLimitAssertions.WasInvokedAtMostOncePer(this IReadOnlyList<DateTimeOffset>, TimeSpan)`** asserts that consecutive timestamps in a recorded invocation log maintain at least the specified minimum interval. The first violating pair fails the assertion with a message naming the violating index, the observed gap, and the required minimum. Empty and single-element sequences pass trivially; the boundary case `gap == interval` passes. Source-generated via `[GenerateAssertion]` so the chain surface is `Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30))`.
- **`TimeAssertions.TimeRenderingHelpers.FormatRateLimitViolation(IReadOnlyList<DateTimeOffset>, int, TimeSpan, TimeSpan)`** renders the multi-line failure message for `WasInvokedAtMostOncePer` violations, with a grep-friendly fixed-unit parenthetical `(gap=Xms, minimum=Yms)` analogous to `FormatBudgetOverrun`'s `(elapsed=, budget=, overrun=)` trailer.

### Changed

- **BREAKING:** **`WithinTimeBudgetAssertion<T>` and `WithinTimeBudgetCapturingAssertion<T>`** now propagate external `OperationCanceledException` instead of wrapping it as an assertion failure. When a parent `[Timeout]` fires or the test runner cancels, the wrapped operation's `OperationCanceledException` flows through the assertion via `ExceptionDispatchInfo.Capture(...).Throw()` so the test is recorded as canceled, not failed. The capturing variant additionally skips invoking the capture callback on cancellation: a partial elapsed from a canceled operation would mislead consumers about the operation's real cost. Non-`OperationCanceledException` source exceptions continue to surface as `AssertionResult.Failed` exactly as before. Consumer tests that asserted `Throws<AssertionException>` against a canceled `WithinTimeBudget` chain must update to expect `Throws<OperationCanceledException>`.
- Bumped `TUnit` / `TUnit.Assertions` / `TUnit.Core` `1.44.0` -> `1.45.8` (and the external-consumer smoke-test pin). The 1.45 line adds `CancellationToken` overloads to upstream `Eventually` / `WaitsFor`; the cookbook section "Waiting for an asynchronous effect after `Advance(...)`" documents the CT-bearing variant. The packed `README.md` requirement line bumps to "TUnit 1.45.8 or later" accordingly.
- README gained an Entry-points subgroup and cookbook section for `WasInvokedAtMostOncePer()` (rate-limit assertions on invocation timestamps, paired with recorded log timestamps for the periodic-probe suppression-window pattern).

## [0.4.0] - 2026-05-13: TimelineRenderer, obsolete alias removal, upstream-Eventually migration cookbook

Minor release. Adds the first concrete renderer under the family-shared `*.Render` namespace convention, fulfils the v0.2.0 commitment to remove the renamed `HasAdvanced` / `HasAdvancedBy` aliases, and documents the canonical upstream polling pattern for consumers crossing async-state-machine boundaries after `FakeTimeProvider.Advance(...)`.

The originally-planned `Eventually(timeout, predicate)` polling primitive (deferred in v0.3.0) is **no longer scoped** for this package: `TUnit.Assertions` ships the same surface as a built-in extension method (`Assert.That(getter).Eventually(assert => assert.IsEqualTo(expected), TimeSpan.FromSeconds(5))`, alias for `WaitsFor`, available since TUnit v1.13.69). A sibling family-side implementation would fragment the polling surface for no net consumer benefit.

### Added

- **`TimeAssertions.Render.TimelineRenderer.Render(DateTimeOffset epoch, IReadOnlyList<TimelineEvent> events)`** renders a sequence of `(Timestamp, Label)` events as deterministic multi-line text suitable for snapshot comparison. Each event renders as `+{deltaMs}ms label` (or `-{absDeltaMs}ms label` for events before the epoch); empty input renders as `string.Empty`. The renderer preserves input order verbatim, including ties on `Timestamp`: caller sorts.
- **`TimeAssertions.Render.TimelineEvent(DateTimeOffset Timestamp, string Label)`** value type (record struct) consumed by the renderer. `Label` is non-null by contract.
- Pairs naturally with `Assert.That(rendered).MatchesSnapshot()` from the sibling `SnapshotAssertions.TUnit` package. The two-line composition is deliberate: `TimeAssertions` does not take a hard dependency on `SnapshotAssertions.TUnit`, so consumers who do not snapshot are unaffected.

### Changed

- New cookbook section "Pinning the moment-graph of a multi-event sequence" in `README.md`: pairs `TimelineRenderer.Render(epoch, events)` with `MatchesSnapshot()` from `SnapshotAssertions.TUnit` and explains why no in-package chain wrapper ships.
- New cookbook section "Waiting for an asynchronous effect after `Advance(...)`" in `README.md`: documents the upstream `Assert.That(getter).Eventually(...)` polling pattern as the canonical replacement for the `Advance` + fixed-yield + `Assert` shape.
- Bumped `Microsoft.Extensions.TimeProvider.Testing` `10.5.0` -> `10.6.0` (verified against [dotnet/extensions#7515](https://github.com/dotnet/extensions/issues/7515); the `HasActiveTimers` proposal remains open, so no new API surface is unlocked by this bump).
- Bumped `Microsoft.SourceLink.GitHub` `10.0.203` -> `10.0.300`.

### Removed

- **`HasAdvanced(this FakeTimeProvider, TimeSpan)`**: the `[Obsolete]` alias carried since v0.2.0 is removed. Migrate to `HasAdvancedExactly(this FakeTimeProvider, TimeSpan)` via search-and-replace.
- **`HasAdvancedBy(this FakeTimeProvider, TimeSpan, TimeSpan)`**: the `[Obsolete]` alias carried since v0.2.0 is removed. Migrate to `HasAdvancedApproximately(this FakeTimeProvider, TimeSpan, TimeSpan)` via search-and-replace.

## [0.3.0] - 2026-05-12: failure-message enrichment, cold-start cookbook

Minor release. Surfaces the grep-friendly elapsed / budget / overrun tuple in every `WithinTimeBudget` failure message and adds a cookbook section for the first-fixture cold-start tax.

### Added

- **`TimeRenderingHelpers.FormatBudgetOverrun`** now appends a grep-friendly uniform-millisecond suffix to its rendered output: `(elapsed=Xms, budget=Yms, overrun=Zms)`. Surfaces in every `WithinTimeBudget` / `WithinTimeBudgetCapturing` failure message and lets CI log scrapers and triage tooling extract the three numbers without parsing the human-readable prose. Behavior-only change to the existing public method; signature unchanged.

### Changed

- New cookbook section "Accommodating first-fixture cold-start" in `README.md`: documents the JIT + DI container build + hosted-service startup tax that pads the first invocation in a freshly-created TUnit fixture, with budget-with-margin and warm-up-call patterns (the latter paired with `WithinTimeBudgetCapturing` for steady-state observability).
- Bumped `TUnit` / `TUnit.Assertions` / `TUnit.Core` `1.43.11` -> `1.44.0`.

## [0.2.0] - 2026-05-07: naming symmetry, elapsed capture, dependency refresh

Feature release. Lockstep version bump for both packages; ApiCompat baseline pinned to 0.1.0.

### Added

- **`HasAdvancedExactly` / `HasAdvancedApproximately`** on `FakeTimeProvider`. Renamed from `HasAdvanced` / `HasAdvancedBy` for symmetry with the rest of the family ("Exactly" vs "Approximately" makes the bounds intent explicit). The original names remain as `[Obsolete]` aliases through v0.3.x and are removed in v0.4.0.
- **`WithinTimeBudgetCapturing(TimeSpan, Action<TimeSpan>)`**: capturing variant of `WithinTimeBudget`. Same wall-clock budget behavior, plus an `Action<TimeSpan>` callback that always receives the measured elapsed (whether the budget was met, exceeded, or the source threw). Useful for tests that need to surface the observed timing in their failure diagnostic before the budget-overrun assertion exception propagates.

### Changed

- Bumped `TUnit` / `TUnit.Assertions` / `TUnit.Core` `1.43.2` -> `1.43.11`.
- Bumped `Microsoft.Extensions.TimeProvider.Testing` `9.5.0` -> `10.5.0`.
- Bumped `Microsoft.SourceLink.GitHub` `8.0.0` -> `10.0.203`.

### Deprecated

- **`HasAdvanced` and `HasAdvancedBy` carry `[Obsolete(error: false)]`.** Two-minor cycle: aliases live through v0.3.x; the v0.4.0 release removes them. Migrate via search-and-replace by name across the test suite.

## [0.1.0] - 2026-05-07: initial release, TUnit-side assertions for TimeProvider-based testable time

First public release, positioned as the TUnit assertion package for projects committed to `TimeProvider`-based testable time. Two packages ship in lockstep: `TimeAssertions` (framework-agnostic core, BCL-only) and `TimeAssertions.TUnit` (TUnit adapter, which transitively ships `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider`). Net 10, AOT-compatible, trimmable, no runtime reflection.

### Added

- **`TimeRenderingHelpers`** (framework-agnostic core): formatting utilities for elapsed durations and budgets in failure-message context. Pure, allocation-conscious.
- **`HasAdvanced(TimeSpan total)`** / **`HasAdvancedBy(TimeSpan total, TimeSpan tolerance)`** (TUnit adapter): assert that the fake provider's current time differs from its construction-time start by exactly `total`, or within `tolerance`. Sanity check for `Advance` / `SetUtcNow` calls in test setup.
- **`HasUtcNow(DateTimeOffset expected)`** / **`HasUtcNowApproximately(DateTimeOffset expected, TimeSpan tolerance)`** (TUnit adapter): assert that `fakeTime.GetUtcNow()` equals the expected moment exactly, or within `tolerance`.
- **`IsRecent(TimeSpan window, TimeProvider? timeProvider = null)`** (TUnit adapter): asserts the timestamp is within the last `window` relative to the supplied `TimeProvider`'s notion of "now"; defaults to `TimeProvider.System` when omitted. Distinct from TUnit core's `IsInPast()` / `IsInFuture()`, which always use the system clock.
- **`IsBeforeNow(TimeProvider timeProvider)`** / **`IsAfterNow(TimeProvider timeProvider)`** (TUnit adapter): strict before-now / after-now checks against the supplied time provider.
- **`WithinTimeBudgetAssertion<T>`** (TUnit adapter): chain extension generating the `WithinTimeBudget(TimeSpan)` assertion. The wall-clock duration captured by TUnit's `EvaluationMetadata<T>.Duration` is compared against the budget; the assertion fails if exceeded. It is post-facto measurement, not cancellation, and composes with any behavioral assertion via `.And`:

  ```csharp
  await Assert.That(asyncOp)
      .IsEqualTo(expectedResult)
      .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
  ```

- **AOT-compatible, trimmable, no runtime reflection** in the assertion path.

[unreleased]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.9.0...HEAD
[0.9.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.8.1...v0.9.0
[0.8.1]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/JohnVerheij/TimeAssertions.TUnit/releases/tag/v0.1.0
