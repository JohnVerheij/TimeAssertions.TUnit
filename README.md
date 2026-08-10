# TimeAssertions.TUnit

[![CI](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/JohnVerheij/TimeAssertions.TUnit/badge)](https://scorecard.dev/viewer/?uri=github.com/JohnVerheij/TimeAssertions.TUnit)
[![codecov](https://codecov.io/gh/JohnVerheij/TimeAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/TimeAssertions.TUnit)
[![NuGet](https://img.shields.io/nuget/v/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

A TUnit-native fluent time-assertion DSL on top of `Microsoft.Extensions.Time.Testing.FakeTimeProvider`. Built using TUnit's `[AssertionExtension]` source generator, so the assertion entry points integrate directly into TUnit's `Assert.That(...)` pipeline. Adds `TimeProvider`-aware `DateTimeOffset` checks plus a cross-cutting `.And.WithinTimeBudget(TimeSpan)` chain extension that composes with any behavioral assertion.

> **Scope:** Test projects only. Not intended for production code.

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family of assertion extensions for TUnit.

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
  - [Rate-limit assertions on invocation timestamps](#rate-limit-assertions-on-invocation-timestamps)
  - [Active-timer leak assertions](#active-timer-leak-assertions)
  - [Counting timer fires](#counting-timer-fires)
  - [Pending-timer due-time assertions](#pending-timer-due-time-assertions)
- [Failure diagnostics](#failure-diagnostics)
- [Cookbook: common patterns](#cookbook-common-patterns)
- [Design notes](#design-notes)
- [Stability intent (pre-1.0)](#stability-intent-pre-10)
- [Limitations and future work](#limitations-and-future-work)
- [Family compatibility](#family-compatibility)
- [Pair with](#pair-with)
- [Contributing](#contributing)
- [License](#license)

---

## Why this package

Asserting on time-dependent behavior during tests typically devolves into either:

- Manual `Assert.True(fakeTime.GetUtcNow() == expected, ...)` plumbing in every test, or
- Real-clock waits (`Thread.Sleep`, `Task.Delay`) with arbitrary tolerances that produce flaky CI when the runner is loaded.

This library replaces both with a fluent DSL on top of Microsoft's recommended `TimeProvider` testability pattern, plus an assertion-level timing-budget extension that composes with any behavioral chain.

## Install

```bash
dotnet add package TimeAssertions.TUnit
```

**Requirements:** TUnit 1.64.6 or later, .NET 10. `TimeAssertions` (the framework-agnostic core) and `Microsoft.Extensions.TimeProvider.Testing` come transitively. The package is AOT-compatible, trimmable, and uses no runtime reflection in the assertion path.

## Package layout

This repo ships **two** NuGet packages:

| Package | Purpose | Depends on |
|---|---|---|
| [`TimeAssertions`](https://www.nuget.org/packages/TimeAssertions/) | Framework-agnostic core: `TimeRenderingHelpers` for elapsed-duration / budget-overrun formatting | BCL only |
| [`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/) | TUnit-specific entry points: `HasAdvancedExactly()`, `HasAdvancedApproximately()`, `HasUtcNow()`, `HasUtcNowApproximately()`, `IsRecent()`, `IsBeforeNow()`, `IsAfterNow()`, `WithinTimeBudget()`, `WithinTimeBudgetCapturing()`, `WasInvokedAtMostOncePer()`, `HasNoActiveTimers()`, `HasActiveTimerCount(int)`, `HasActiveTimers()`, `HasAtLeastActiveTimerCount(int)`, `HasNoActiveTimersEventually()`, `HasActiveTimerCountEventually()`, `HasAtLeastActiveTimerCountEventually()`, `HasAtMostActiveTimerCountEventually()`, `HasActiveTimersEventually()`, `HasNextTimerDueApproximately()`, `HasPendingTimerDueWithin()`, `HasTimerFiredCount(int)`, `HasNoTimerFired()`, `HasTimerFiredAtLeast(int)` | `TimeAssertions` + `TUnit.Assertions` + `TUnit.Core` + `Microsoft.Extensions.TimeProvider.Testing` |

You install `TimeAssertions.TUnit`; `TimeAssertions` and `Microsoft.Extensions.TimeProvider.Testing` come transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are *not* shipped today: they would reuse the `TimeAssertions` core. Open a feature request if you need one.

## Namespaces (and a `GlobalUsings.cs` recommendation)

The two packages place types in two namespaces with deliberately-different scopes:

| Type / member | Namespace | Auto-imported? |
|---|---|---|
| `HasAdvancedExactly()`, `HasAdvancedApproximately()`, `HasUtcNow()`, `HasUtcNowApproximately()`, `IsRecent()`, `IsBeforeNow()`, `IsAfterNow()`, `WithinTimeBudget()`, `WithinTimeBudgetCapturing()`, `WasInvokedAtMostOncePer()`, `HasNoActiveTimers()`, `HasActiveTimerCount(int)`, `HasActiveTimers()`, `HasAtLeastActiveTimerCount(int)`, `HasNoActiveTimersEventually()`, `HasActiveTimerCountEventually()`, `HasAtLeastActiveTimerCountEventually()`, `HasAtMostActiveTimerCountEventually()`, `HasActiveTimersEventually()`, `HasNextTimerDueApproximately()`, `HasPendingTimerDueWithin()`, `HasTimerFiredCount(int)`, `HasNoTimerFired()`, `HasTimerFiredAtLeast(int)` (source-generated entries) | `TUnit.Assertions.Extensions` | **Yes**: TUnit auto-imports |
| `FakeTimeProvider` (the testable-clock type) | `Microsoft.Extensions.Time.Testing` | **No**: needed at the call site; recommended for `GlobalUsings.cs` |
| `TimeRenderingHelpers` (formatting utilities for failure messages) | `TimeAssertions` | **No**: needed at the call site; recommended for `GlobalUsings.cs` |
| `WithinTimeBudgetAssertion<T>`, `WithinTimeBudgetCapturingAssertion<T>` (the assertion classes behind `WithinTimeBudget()` and `WithinTimeBudgetCapturing()`) | `TimeAssertions.TUnit` | **No**: needed at the call site; recommended for `GlobalUsings.cs` |

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
        await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(31));
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
3. **Tests call `fakeTime.Advance(TimeSpan)` or `fakeTime.SetUtcNow(...)`** to drive time forward deterministically: no `Thread.Sleep`, no flaky timing, no waiting for real wall-clock seconds to pass.
4. **Tests assert** that production code reacted correctly to the simulated time.

This package supplies the assertion side of step 4. Without it, you write boilerplate (`Assert.True(fakeTime.GetUtcNow() == expected, ...)`) for every time-dependent test. With it:

```csharp
await Assert.That(fakeTime).HasUtcNow(expected);
await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(5));
await Assert.That(timestamp).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
await Assert.That(timestamp).IsBeforeNow(fakeTime);
await Assert.That(timestamp).IsAfterNow(fakeTime);
```

For projects standardising on this pattern, TimeAssertions.TUnit is the TUnit-side test infrastructure that pays for itself test-by-test.

---

## Entry points

Six groups of entry points cover six distinct testing concerns: fake-clock state, `TimeProvider`-aware `DateTimeOffset` checks, assertion-level timing budgets, rate-limit assertions on invocation timestamps, timer-leak detection, and pending-timer due-time inspection.

### `FakeTimeProvider` state assertions

| Entry point | Behavior |
|---|---|
| `HasAdvancedExactly(TimeSpan total)` | Asserts `fakeTime.GetUtcNow() - construction-time` equals `total` exactly. Sanity check for `Advance` / `SetUtcNow` calls in test setup. |
| `HasAdvancedApproximately(TimeSpan total, TimeSpan tolerance)` | Same, with absolute tolerance. Useful when production code performs additional internal `Advance` calls. |
| `HasUtcNow(DateTimeOffset expected)` | Asserts `fakeTime.GetUtcNow()` equals `expected` exactly. |
| `HasUtcNowApproximately(DateTimeOffset expected, TimeSpan tolerance)` | Same, with absolute tolerance. Useful when the expected moment is computed from integer-truncated minute math or chained `Advance` calls with rounding rather than a literal. |

```csharp
var fakeTime = new FakeTimeProvider();
fakeTime.Advance(TimeSpan.FromHours(2));

await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromHours(2));
```

> **Renamed in v0.2.0.** The previous names `HasAdvanced` / `HasAdvancedBy` are kept as `[Obsolete]` aliases through v0.3.x and removed in v0.4.0. The rename gives both names an explicit "Exactly" vs "Approximately" suffix for symmetry with `HasUtcNow` / `HasUtcNowApproximately`. Migrate via search-and-replace by name across the test suite.

### `TimeProvider`-aware `DateTimeOffset` assertions

Distinct from TUnit core's `IsInPast()` / `IsInFuture()` (which always use the system clock):

| Entry point | Behavior |
|---|---|
| `IsRecent(TimeSpan window, TimeProvider? timeProvider = null)` | Asserts the timestamp is within the last `window` relative to the supplied `TimeProvider`'s notion of "now". When `timeProvider` is `null` or omitted, falls back to `TimeProvider.System` (useful for end-to-end tests not running under a fake clock). |
| `IsBeforeNow(TimeProvider timeProvider)` | Strict-before-now check against the supplied time provider. |
| `IsAfterNow(TimeProvider timeProvider)` | Strict-after-now check. |

```csharp
await Assert.That(service.LastProcessedAt).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
await Assert.That(record.ExpiresAt).IsBeforeNow(fakeTime);
await Assert.That(service.NextRunAt).IsAfterNow(fakeTime);
```

### Cross-cutting timing budget

`.And.WithinTimeBudget(TimeSpan)` composes with **any** behavioral assertion. The wall-clock duration captured by TUnit's `EvaluationMetadata<T>.Duration` is compared against the budget; the chain fails if exceeded.

```csharp
// Canonical pattern: .And.WithinTimeBudget(...) after any behavioral assertion
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

#### Capturing the elapsed time: `WithinTimeBudgetCapturing` (v0.2.0+)

When you need the measured elapsed value (e.g. to log it, or to feed it into a follow-up assertion), use `WithinTimeBudgetCapturing(TimeSpan budget, Action<TimeSpan> capture)`. Same wall-clock-budget behavior as `WithinTimeBudget`, plus an `Action<TimeSpan>` callback that receives the measured elapsed on every evaluation path EXCEPT external cancellation (since v0.5.0): whether the budget passed, was exceeded, or the source threw a non-`OperationCanceledException`. See the paragraph after the example for the cancellation contract.

```csharp
var elapsed = TimeSpan.Zero;
await Assert.That(asyncOp)
    .IsEqualTo(expectedResult)
    .And.WithinTimeBudgetCapturing(TimeSpan.FromMilliseconds(500), e => elapsed = e);

// 'elapsed' now holds the wall-clock duration of the asyncOp evaluator.
// Use it for diagnostic logging, or feed into HasAdvancedApproximately for
// a follow-up assertion against a fake clock advanced by the same amount.
TestContext.Current.OutputWriter.WriteLine($"asyncOp took {elapsed.TotalMilliseconds:F1}ms");
```

The capture callback runs on **every** evaluation path EXCEPT external cancellation (since v0.5.0), so failed-budget tests can still surface the observed timing in their failure diagnostic before the budget-overrun `AssertionException` propagates. If the source itself threw a non-`OperationCanceledException`, the callback receives the partial elapsed reported by TUnit's `EvaluationMetadata<T>.Duration`. When the source threw `OperationCanceledException` (parent `[Timeout]`, test-class CT, runner cancel), the assertion propagates the OCE to the test runner and the capture callback is deliberately not invoked: a partial elapsed from a canceled operation would mislead consumers about the operation's real cost.

### Rate-limit assertions on invocation timestamps

`WasInvokedAtMostOncePer(TimeSpan interval)` asserts that consecutive timestamps in a recorded invocation log maintain at least the specified minimum interval. The classic use case is a periodic-probe contract: "the failure handler must fire at most once per 30 seconds; subsequent failures inside that window are suppressed".

| Entry point | Behavior |
|---|---|
| `WasInvokedAtMostOncePer(this IReadOnlyList<DateTimeOffset> timestamps, TimeSpan interval)` | Asserts every consecutive pair `(timestamps[i-1], timestamps[i])` is at least `interval` apart. The first violating pair fails the assertion with a message naming the violating index, observed gap, and required minimum. Empty / single-element sequences pass trivially; the boundary case `gap == interval` passes (minimum is inclusive). |

```csharp
// Production code records invocation timestamps somewhere observable; the test
// extracts the timestamp list from that recording and asserts the rate-limit.
List<DateTimeOffset> failureLogs = collector.Collected
    .Where(r => r.Message.Contains("PingFailed", StringComparison.Ordinal))
    .Select(r => r.Timestamp)
    .ToList();

await Assert.That(failureLogs).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
```

The receiver is the recorded log itself, NOT the action being invoked: the consumer's production code calls the rate-limited operation, the test records each invocation's timestamp, and the assertion examines the recording. Caller is responsible for chronological order; the assertion preserves input order verbatim.

### Active-timer leak assertions

`HasNoActiveTimers()` and `HasActiveTimerCount(int)` assert on timer disposal: did a `BackgroundService` / `IHostedService` dispose every `ITimer` it started? `FakeTimeProvider` does not surface the timers created against it ([dotnet/extensions#7515](https://github.com/dotnet/extensions/issues/7515)), so wrap it in the framework-agnostic `ObservableTimeProvider` (shipped in the `TimeAssertions` core package), run the code under test, then assert.

| Entry point | Behavior |
|---|---|
| `HasNoActiveTimers()` | Asserts every timer created through the `ObservableTimeProvider` has been disposed. On failure the message names each survivor by its schedule (`[dueTime=..., period=...]`; one-shot timers as `period=one-shot`) with a grep-friendly `(count=N)` trailer, instead of a bare integer. |
| `HasActiveTimerCount(int expected)` | Asserts the exact number of active (undisposed) timers: the registration half of a disposal test. On mismatch the message renders expected vs actual counts plus each active timer's schedule, with an `(expected=N, actual=M)` trailer. |
| `HasActiveTimers()` | Asserts at least one timer is active: the positive-presence counterpart for the registration half of a disposal test, without pinning the exact count. |
| `HasAtLeastActiveTimerCount(int count)` | Asserts the active count is at least `count`, for a lower bound rather than an exact count. On a shortfall the message renders the required minimum vs actual count plus each active timer's schedule, with a `(minimum=N, actual=M)` trailer. |
| `HasNoActiveTimersEventually(TimeSpan timeout, ...)` | Polls the active count on the real wall clock until it reaches zero, or `timeout` elapses. For the asynchronous disposal race a synchronous check cannot see (see below). On timeout the message names each survivor by its schedule with a `(count=N)` trailer. |
| `HasActiveTimerCountEventually(int count, TimeSpan timeout, ...)` | The count-targeted sibling: polls until the active count equals `count`, or `timeout` elapses. On timeout the message renders the expected and actual counts with an `(expected=N, actual=M)` trailer. |
| `HasAtLeastActiveTimerCountEventually(int count, TimeSpan timeout, ...)` | The asynchronous lower-bound sibling: polls until the active count is at least `count`, or `timeout` elapses. The right shape for an asynchronous registration wait where more than one timer may register. On timeout the message renders the minimum and actual counts with a `(minimum=N, actual=M)` trailer. |
| `HasAtMostActiveTimerCountEventually(int count, TimeSpan timeout, ...)` | The asynchronous upper-bound sibling: polls until the active count is at most `count`, or `timeout` elapses, for "the active set settles to no more than N". On timeout the message renders the maximum and actual counts with a `(maximum=N, actual=M)` trailer. `HasAtMostActiveTimerCountEventually(0, ...)` is equivalent to `HasNoActiveTimersEventually`. |
| `HasActiveTimersEventually(TimeSpan timeout, ...)` | The asynchronous counterpart of `HasActiveTimers()`: polls until at least one timer is active, or `timeout` elapses. A named shorthand for `HasAtLeastActiveTimerCountEventually(1, ...)`. |

```csharp
var time = new ObservableTimeProvider(new FakeTimeProvider());
var service = new HeartbeatService(time);

await service.StartAsync(ct);
await Assert.That(time).HasActiveTimerCount(1);   // the heartbeat timer registered

await service.StopAsync(ct);
await Assert.That(time).HasNoActiveTimers();       // ...and was disposed on stop
```

An `IHostedService` commonly disposes its timer on a continuation that runs *after* `StopAsync` returns to the caller, so a synchronous `HasNoActiveTimers()` just after stop can still see the timer. `HasNoActiveTimersEventually(timeout)` handles that race: it polls the live active count on the real wall clock until it reaches zero, giving the pending disposal continuation time to run.

```csharp
await service.StopAsync(ct);
await Assert.That(time).HasNoActiveTimersEventually(TimeSpan.FromSeconds(2));
```

The poll uses a real `Task.Delay` loop against a wall-clock deadline, not a fake-time advance: disposal happens on a real asynchronous continuation, which a fake clock cannot drive. The default poll interval is 10 ms (override it with the optional `pollingInterval`); the condition is checked once before the first delay, so an already-clean provider passes without waiting. Pass a `CancellationToken` to honor an external cancel; it can follow the timeout positionally, `HasNoActiveTimersEventually(timeout, cancellationToken)`, without the named `cancellationToken:` form. `HasActiveTimerCountEventually(count, timeout)` is the same shape when the active set settles to a non-zero steady state, and `HasAtLeastActiveTimerCountEventually(count, timeout)`, `HasAtMostActiveTimerCountEventually(count, timeout)`, and `HasActiveTimersEventually(timeout)` poll for a lower bound, an upper bound, and at least one active timer respectively.

#### Migrating from a hand-rolled `ObservableTimeProvider`

If you already wrap `FakeTimeProvider` in your own tracking decorator, note one deliberate shape difference: `ActiveTimers` returns `IReadOnlyList<ActiveTimerInfo>` describing each timer's **schedule** (`DueTime` / `Period`), not the `ITimer` references themselves. That is intentional. A leak diagnostic needs to answer "which timer survived, and what was it scheduled to do", and the schedule is exactly that answer; an `ITimer` identity is not portable across runs and carries no diagnostic value in a failure message. Exposing only the schedule also keeps the snapshot immutable: the returned list is a point-in-time copy that later creations or disposals do not mutate.

### Counting timer fires

The leak assertions cover timer disposal. These cover how many times a timer's callback actually ran. `ObservableTimeProvider` counts every callback fire across every timer it created. The count is cumulative and is not reset on disposal, so a timer that fired three times and was then stopped still reports `3`. With a `FakeTimeProvider`, a fire is counted each time test code advances fake time past a due or period boundary, which keeps the count deterministic.

| Entry point | Behavior |
|---|---|
| `HasTimerFiredCount(int expected)` | Asserts the cumulative fire count equals `expected`: "advancing two periods fired the heartbeat twice." On mismatch the message renders expected vs actual with an `(expected=N, actual=M)` trailer, plus each still-active timer's schedule and per-timer fire count. |
| `HasNoTimerFired()` | Asserts no callback has fired: the timer was scheduled but fake time was not advanced far enough to trigger it. On failure the message renders the cumulative count with an `(expected=0, actual=M)` trailer. |
| `HasTimerFiredAtLeast(int count)` | Asserts the cumulative fire count is at least `count`: the liveness lower bound for a heartbeat whose exact fire count is subject to timing jitter. On a shortfall the message renders the minimum vs actual with a `(minimum=N, actual=M)` trailer. |

```csharp
var fakeTime = new FakeTimeProvider();
var time = new ObservableTimeProvider(fakeTime);
var service = new HeartbeatService(time);   // CreateTimer(_, _, dueTime: 1s, period: 1s)
await service.StartAsync(ct);

await Assert.That(time).HasNoTimerFired();             // scheduled, not yet fired
fakeTime.Advance(TimeSpan.FromSeconds(3));             // advance the inner fake clock
await Assert.That(time).HasTimerFiredCount(3);         // the loop ran three times
```

Advance fake time on the inner `FakeTimeProvider` you wrapped (keep a reference to it, as above); `ObservableTimeProvider` forwards every clock operation to it. Once a periodic timer fires, `NextTimerDueTime` (and the `HasNextTimerDueApproximately` / `HasPendingTimerDueWithin` assertions) report the timer's period, since that is when the next callback is due. A one-shot timer that has fired is disabled and drops out of the pending-due calculation.

### Pending-timer due-time assertions

`HasNextTimerDueApproximately(expected, tolerance)` and `HasPendingTimerDueWithin(min, max)` inspect the schedule a pending timer carries on an `ObservableTimeProvider` **without advancing the clock**, so a test can verify which delay a loop just scheduled (for example a step of an exponential backoff) rather than advancing fake time and inferring the delay from when the callback fires. The "next" timer is the one with the smallest due time among the enabled (non-infinite) active timers; the underlying `ObservableTimeProvider.NextTimerDueTime` property exposes that value (or `null` when no enabled timer is pending).

| Entry point | Behavior |
|---|---|
| `HasNextTimerDueApproximately(TimeSpan expected, TimeSpan tolerance)` | Asserts the next pending timer's due time is within `tolerance` of `expected`. On failure the message names the expected and observed due times and the delta, with a grep-friendly `(expected=Xms, tolerance=Yms, actual=Zms, delta=Wms)` trailer, or `actual=none` when no enabled timer is pending. |
| `HasPendingTimerDueWithin(TimeSpan min, TimeSpan max)` | Asserts the next pending timer's due time falls within the inclusive range `[min, max]`. On failure the message names the range and observed due time, with a `(min=Xms, max=Yms, actual=Zms)` trailer, or `actual=none` when no enabled timer is pending. |

```csharp
var time = new ObservableTimeProvider(new FakeTimeProvider());
var client = new ReconnectingClient(time);

await client.OnDisconnect();   // first reconnect attempt scheduled
// Assert the scheduled backoff delay directly, without advancing the clock:
await Assert.That(time).HasNextTimerDueApproximately(
    TimeSpan.FromMilliseconds(500), tolerance: TimeSpan.FromMilliseconds(1));
```

---

## Failure diagnostics

Failures render the actual measurement against the expected value, with no extra `Console.WriteLine` calls needed.

**`HasAdvancedExactly` mismatch:**

```text
Expected:
  fakeTime to have advanced 31m

Actual:
  advanced 30m (differs by 1m)
```

**`WithinTimeBudget` budget exceeded (assertion behavioral check passed but slow):**

```text
Expected:
  to be equal to 42
  and completion within timing budget of 500ms

Actual:
  Value: 42 (matches)
  Timing: completed in 1.2s: exceeded budget of 500ms by 747ms
```

**Source threw (timing surface is additive; a thrown source is the dominant failure mode):**

```text
Expected:
  to be equal to 42
  and completion within timing budget of 500ms

Actual:
  Source threw InvalidOperationException: connection refused
```

The budget-overrun rendering carries a grep-friendly fixed-unit suffix `(elapsed=Xms, budget=Yms, overrun=Zms)` in addition to the human-readable prose, so CI log scrapers and triage tooling can extract the three numbers without parsing the prose.

For tests that need to surface the measured elapsed even on the success path, the capturing variant `WithinTimeBudgetCapturing` invokes its callback on every evaluation path (pass / fail / throw). Use it to write the observed elapsed to test output before the assertion exception propagates: see [Capturing the elapsed time](#capturing-the-elapsed-time-withintimebudgetcapturing-v020).

---

## Cookbook: common patterns

### Production code accepts `TimeProvider`; tests inject `FakeTimeProvider`

A complete production-code + test pair showing how `TimeProvider` injection, `FakeTimeProvider`, and these assertions compose. The production code never reads the system clock directly: every time-dependent decision goes through the injected `TimeProvider`. Tests inject `FakeTimeProvider`, drive time deterministically, and assert against fake-clock state.

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
- **Both sides assertable.** `HasUtcNow` / `HasAdvancedExactly` / `HasAdvancedApproximately` confirm the *fake clock's* state; `IsRecent` / `IsBeforeNow` / `IsAfterNow` confirm *production state* relative to that fake clock.
- **No system-clock leakage.** Because production code accepts `TimeProvider` and the test injects `FakeTimeProvider`, there's no path where `DateTimeOffset.UtcNow` could sneak in.

### System clock `IsRecent` (no TimeProvider)

`IsRecent`'s `TimeProvider` parameter is optional: when omitted, `TimeProvider.System` is used. Useful for end-to-end tests that don't run under a fake clock:

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
await Assert.That(fakeTime).HasAdvancedApproximately(
    total: TimeSpan.FromMinutes(30),
    tolerance: TimeSpan.FromSeconds(1));
```

### Waiting for an asynchronous effect after `Advance(...)`

After advancing `FakeTimeProvider`, timer callbacks and any continuations they trigger run on real wall-clock thread-pool threads; they do not complete synchronously when `Advance` returns. The naïve pattern is a fixed `Task.Delay` between the advance and the assert:

```csharp
// Pattern to avoid: brittle and slow.
fakeTime.Advance(TimeSpan.FromMinutes(2));
await Task.Delay(TimeSpan.FromMilliseconds(50), ct);  // hope continuations have drained
await Assert.That(collector.HasLogged("[Heartbeat]")).IsTrue();
```

The 50ms guess is paid on every test run regardless of whether continuations drained in 5ms or 500ms. Replace it with TUnit's built-in [`Eventually`](https://github.com/thomhurst/TUnit) polling assertion, which re-evaluates the source until the inner assertion passes or the timeout elapses:

```csharp
fakeTime.Advance(TimeSpan.FromMinutes(2));
await Assert.That(() => collector.HasLogged("[Heartbeat]"))
    .Eventually(a => a.IsTrue(), TimeSpan.FromSeconds(1));
```

`Eventually` uses a 10ms default polling interval, so the median case completes in 10-20ms instead of the worst-case 50ms. Use generic `Eventually` (or its alias `WaitsFor`) for an arbitrary condition like a logged line or an externally-updated counter, where no domain assertion fits. For the active-timer disposal race, the package ships dedicated polling overloads (`HasNoActiveTimersEventually` and its count-targeted siblings, see [Active-timer leak assertions](#active-timer-leak-assertions)). They assert on the provider and produce a survivor-naming failure message, so prefer them over a hand-rolled `Eventually` on `ActiveTimerCount`.

For polling sources updated externally (e.g. a counter incremented by a different thread), the same pattern applies with an `int`-typed source and a value predicate:

```csharp
await Assert.That(() => observable.ActiveTimerCount)
    .Eventually(a => a.IsGreaterThanOrEqualTo(1),
                timeout: TimeSpan.FromSeconds(5),
                pollingInterval: TimeSpan.FromMilliseconds(25));
```

Since TUnit 1.45.0, both `Eventually` and its alias `WaitsFor` accept a trailing `CancellationToken`. Plumb the test's own token so that an external cancel (parent `[Timeout]`, test-class CT, runner cancel) aborts the polling loop instead of waiting for the configured timeout argument:

```csharp
[Test]
public async Task Heartbeat_fires_before_parent_timeout(CancellationToken cancellationToken)
{
    fakeTime.Advance(TimeSpan.FromMinutes(2));
    await Assert.That(() => collector.HasLogged("[Heartbeat]"))
        .Eventually(a => a.IsTrue(),
                    timeout: TimeSpan.FromSeconds(10),
                    cancellationToken: cancellationToken);
}
```

The CT short-circuits the polling loop on external cancellation; the timeout argument remains the upper bound for the no-cancel case. The `WithinTimeBudget` / `WithinTimeBudgetCapturing` chains in this package also propagate `OperationCanceledException` intact since v0.5.0, so a chain like `Eventually(...).And.WithinTimeBudget(...)` surfaces cancellation as a canceled test rather than an assertion failure.

### Pinning the moment-graph of a multi-event sequence

When a test produces a sequence of named events at known fake-time moments, snapshot the whole graph rather than asserting on each event individually. `TimelineRenderer` produces a deterministic byte-stable string from a list of `(Timestamp, Label)` pairs; pair it with `MatchesSnapshot()` from `SnapshotAssertions.TUnit` to pin the graph against a committed baseline.

```csharp
using TimeAssertions.Render;

[Test]
public async Task HeartbeatService_emits_at_expected_cadence(CancellationToken ct)
{
    var epoch = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);
    var fakeTime = new FakeTimeProvider();
    fakeTime.SetUtcNow(epoch);
    var events = new List<TimelineEvent>();

    var service = new HeartbeatService(fakeTime, ev => events.Add(new TimelineEvent(fakeTime.GetUtcNow(), ev)));
    await service.StartAsync(ct);
    fakeTime.Advance(TimeSpan.FromMinutes(3));

    var rendered = TimelineRenderer.Render(epoch, events);
    // rendered =
    //   +60000ms Heartbeat
    //   +120000ms Heartbeat
    //   +180000ms Heartbeat
    await Assert.That(rendered).MatchesSnapshot();
}
```

`MatchesSnapshot()` lives in the sibling [`SnapshotAssertions.TUnit`](https://www.nuget.org/packages/SnapshotAssertions.TUnit/) package; this package does not take a hard dependency on it. The two-line composition (`Render`, then assert) lets consumers reach for the renderer without committing to a specific snapshot framework, and lets `SnapshotAssertions.TUnit` stay an opt-in pairing.

The renderer preserves input order verbatim, including ties on `Timestamp`. If the snapshot needs a specific ordering (chronological, by-category, etc.) the caller sorts the input list before rendering.

### Capturing the elapsed time of a behavioral assertion

```csharp
var elapsed = TimeSpan.Zero;
await Assert.That(httpClient.GetAsync("/health"))
    .CompletesSuccessfully()
    .And.WithinTimeBudgetCapturing(TimeSpan.FromSeconds(2), e => elapsed = e);

// Surface the measured latency in test output, even when the assertion passes.
TestContext.Current.OutputWriter.WriteLine($"GET /health: {elapsed.TotalMilliseconds:F1}ms");
```

### Verifying a periodic-probe suppression window

A common production pattern: a background component periodically probes some external resource (HTTP health endpoint, message broker, database connectivity). When the probe fails, the first failure is logged; subsequent failures inside a suppression window are deliberately silenced so a sustained outage does not flood the log. The contract to verify is "the failure handler logs at most once per `<window>` seconds".

`WasInvokedAtMostOncePer` asserts the consecutive-pair gap against a minimum interval. Extract the failure timestamps from whatever recording mechanism the test uses (a `FakeLogCollector`, a captured event probe, a list populated in a wrapped callback) and assert against the recording.

```csharp
[Test]
public async Task PingHandler_suppresses_repeated_failures_within_30s_window(CancellationToken ct)
{
    var fakeTime = new FakeTimeProvider();
    var collector = new FakeLogCollector();
    var handler = new PingHandler(fakeTime, collector.GetLogger<PingHandler>());

    // Simulate ten consecutive ping failures spaced 5s apart of fake time.
    for (var i = 0; i < 10; i++)
    {
        await handler.HandleFailureAsync(ct);
        fakeTime.Advance(TimeSpan.FromSeconds(5));
    }

    // Production code logs every failure as "[PingFailed]" but suppresses the second-and-later
    // occurrence within any 30-second window. After ten failures spaced 5s apart, we expect at
    // most one log entry per 30-second window: two failures total (at t=0 and t=30).
    var failureTimestamps = collector.Collected
        .Where(r => r.Message.Contains("[PingFailed]", StringComparison.Ordinal))
        .Select(r => r.Timestamp)
        .ToList();

    await Assert.That(failureTimestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
}
```

The assertion preserves input order. If the underlying mechanism does not guarantee chronological order (rare; log collectors usually do), sort before asserting. The failure message names the first violating index, the observed gap, and the required minimum so a regression in the suppression-window code path is immediately legible:

```text
Expected:
  to have at most one invocation per 30s

Actual:
  interval violation at index 4: gap was 5.0s (minimum 30s)
    timestamps[3]: 2026-01-01T00:00:15.000+00:00
    timestamps[4]: 2026-01-01T00:00:20.000+00:00
    (gap=5000ms, minimum=30000ms)
```

`WasInvokedAtMostOncePer` is added in v0.5.0; consumers on earlier versions can hand-roll the equivalent gap check inline.

### Accommodating first-fixture cold-start

`WithinTimeBudget` measures wall-clock time including JIT compilation, DI container construction, hosted-service startup, and any one-shot lazy initialization that happens during the first call in a freshly-created fixture. The cold-start tax varies by workload and runner; for hosted-service-backed pipelines on shared CI it can be several multiples of the steady-state cost. Measure locally to calibrate before setting tight budgets.

Two patterns address this:

**Pattern A: budget with margin (simple).** Set the budget at 5-10x the local steady-state measurement on paths that exercise hosted-service startup, DI container build, or first-time JIT. The goal of `WithinTimeBudget` is to catch order-of-magnitude regressions, not micro-benchmark drift.

```csharp
// Local steady-state: ~1s. Cold-start: up to 5s. Budget 10s for order-of-magnitude regression detection.
await Assert.That(action)
    .ThrowsNothing()
    .And.WithinTimeBudget(TimeSpan.FromSeconds(10));
```

**Pattern B: warm-up call (precise).** Factor a single warm-up invocation before the measured call. The warm-up amortises JIT and one-shot init; the measured call sees steady-state cost only.

```csharp
// Warm up: pays the cold-start tax once, discarded.
await action();

// Measured: now reflects steady-state cost only.
await Assert.That(action)
    .ThrowsNothing()
    .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
```

Pair Pattern B with `WithinTimeBudgetCapturing` to log the actual measured elapsed and confirm the steady-state assumption holds:

```csharp
await action(); // warm up

var elapsed = TimeSpan.Zero;
await Assert.That(action)
    .ThrowsNothing()
    .And.WithinTimeBudgetCapturing(TimeSpan.FromMilliseconds(500), e => elapsed = e);
TestContext.Current.OutputWriter.WriteLine($"steady-state: {elapsed.TotalMilliseconds:F1}ms");
```

---

## Design notes

- **`WithinTimeBudget`, not `Within`.** TUnit core already uses `.Within(...)` for tolerance assertions, so a separate name avoids the overload collision and reads naturally after `.And`. It is generic in the source's value type: `.And.WithinTimeBudget(...)` infers it, while the direct `.WithinTimeBudget<int>(...)` form needs an explicit type argument.
- **`IsBeforeNow` / `IsAfterNow`, not `IsInPast` / `IsInFuture`.** TUnit core's versions use the system clock; ours take a `TimeProvider`. Distinct names make the mechanism obvious: use TUnit's for system-clock tests, ours for `FakeTimeProvider`-driven ones.
- **`Microsoft.Extensions.TimeProvider.Testing` is propagated, not `PrivateAssets="all"`.** Consumers asserting on `FakeTimeProvider` need it in scope; making it transitive saves an explicit reference per test project.

## Stability intent (pre-1.0)

This is a 0.x release and the public API may evolve. Specifically:

- **Additive changes** (new entry points, new shorthand wrappers, new tolerance overloads) ship in any patch / minor without breaking ApiCompat.
- **Breaking changes** to existing signatures bump the minor version (0.X.0) and are called out in the [CHANGELOG](CHANGELOG.md).
- **`PackageValidationBaselineVersion`** is pinned to the previous shipped version starting from 0.1.1, so ApiCompat breakage is caught at pack time.

The 1.0 milestone signals API stability: see [Limitations and future work](#limitations-and-future-work) for what's still being designed.

## Limitations and future work

Deferred (demand-driven): a `Stopwatch.GetTimestamp()`-based monotonic-clock variant of `WithinTimeBudget`, for benchmark-class precision. Today `WithinTimeBudget` uses TUnit's `EvaluationMetadata<T>.Duration` (`DateTimeOffset.Now`-based); system-clock jumps mid-test are vanishingly rare.

## Family compatibility

The nine assertion-family packages: `LogAssertions.TUnit`, `TimeAssertions.TUnit`, `SnapshotAssertions.TUnit`, `MathAssertions.TUnit`, `JsonAssertions.TUnit`, `SseAssertions.TUnit`, `GrpcAssertions.TUnit`, `TracingAssertions.TUnit`, and `MetricsAssertions.TUnit`: release independently and target the same .NET TFM at any moment (LTS-anchored, multi-target during STS support windows; see the [TFM policy in CONVENTIONS.md](CONVENTIONS.md#tfm-policy) for the rotation schedule). **Mix versions freely.** Each package ships under SemVer with `EnablePackageValidation` strict-mode ApiCompat against its previous baseline, so binary breaks within a version line are caught at pack time.

For per-package release notes:
- [LogAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/CHANGELOG.md)
- [TimeAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/CHANGELOG.md)
- [SnapshotAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/SnapshotAssertions.TUnit/blob/main/CHANGELOG.md)
- [MathAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/MathAssertions.TUnit/blob/main/CHANGELOG.md)
- [JsonAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/JsonAssertions.TUnit/blob/main/CHANGELOG.md)
- [SseAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/CHANGELOG.md)
- [GrpcAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/GrpcAssertions.TUnit/blob/main/CHANGELOG.md)
- [TracingAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/TracingAssertions.TUnit/blob/main/CHANGELOG.md)
- [MetricsAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/MetricsAssertions.TUnit/blob/main/CHANGELOG.md)

## Pair with

- **[`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/)**: fluent log assertions over `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. Use `.And.WithinTimeBudget(...)` to add a timing budget to any `HasLogged()` chain.
- **[`SnapshotAssertions.TUnit`](https://www.nuget.org/packages/SnapshotAssertions.TUnit/)**: text-snapshot assertions for API-surface tests and similar deterministic-string scenarios. Coexists with Verify; covers the 80% case without coverage friction.
- **[`MathAssertions.TUnit`](https://www.nuget.org/packages/MathAssertions.TUnit/)**: tolerance-aware fluent assertions over numeric and geometric types (vectors, quaternions, matrices, planes, complex numbers, arrays).
- **[`JsonAssertions.TUnit`](https://www.nuget.org/packages/JsonAssertions.TUnit/)**: fluent JSON assertions over `System.Text.Json`, HTTP response bodies (including RFC 7807 ProblemDetails), and source-generated `JsonSerializerContext` registration.
- **[`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/)**: Server-Sent Events (SSE) wire-format assertions: event-count, field shape (`event:`, `data:`, `id:`, `retry:`), and stream content validation.
- **[`GrpcAssertions.TUnit`](https://www.nuget.org/packages/GrpcAssertions.TUnit/)**: fluent gRPC outcome assertions (`ThrowsGrpcException` with `StatusCode` shorthands and detail refinements) plus the `GrpcCallBuilder` test-double helper.
- **[`TracingAssertions.TUnit`](https://www.nuget.org/packages/TracingAssertions.TUnit/)**: fluent OpenTelemetry distributed-tracing (`Activity` / span) assertions: operation name, tags, status, and parent/child and same-trace relationships, captured via a raw `ActivityListener` with no OpenTelemetry SDK dependency.
- **[`MetricsAssertions.TUnit`](https://www.nuget.org/packages/MetricsAssertions.TUnit/)**: fluent assertions over `System.Diagnostics.Metrics` instruments (counters, histograms, gauges), built on `MetricCollector`.

## Contributing

Issues and pull requests welcome. Before opening a PR:

- Run `dotnet build` and `dotnet test` locally; the CI pipeline enforces the same quality bar (zero warnings as errors, 90% line / 90% branch coverage minimum).
- Match the existing code style (`.editorconfig` is authoritative; `dotnet format` covers formatting).
- For new assertions, include a test for both the happy path and a representative failure case so the failure-message rendering is verified.

For larger ideas (new entry points, breaking changes, cross-cutting refactors), open a [Discussion](https://github.com/JohnVerheij/TimeAssertions.TUnit/discussions) first to align on direction before investing implementation time.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full PR review checklist and API design principles, and [CONVENTIONS.md](CONVENTIONS.md) for the family-wide code conventions shared across the assertion family.

## License

[MIT](LICENSE). Copyright (c) 2026 John Verheij.
