# TimeAssertions.TUnit

[![CI](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/TimeAssertions.TUnit/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/JohnVerheij/TimeAssertions.TUnit/badge)](https://scorecard.dev/viewer/?uri=github.com/JohnVerheij/TimeAssertions.TUnit)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/16144/badge)](https://www.bestpractices.dev/projects/16144)
[![codecov](https://codecov.io/gh/JohnVerheij/TimeAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/TimeAssertions.TUnit)
[![NuGet](https://img.shields.io/nuget/v/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

A TUnit-native fluent time-assertion DSL on top of `Microsoft.Extensions.Time.Testing.FakeTimeProvider`. Built using TUnit's `[AssertionExtension]` source generator, so the assertion entry points integrate directly into TUnit's `Assert.That(...)` pipeline. Adds `TimeProvider`-aware `DateTimeOffset` checks plus a cross-cutting `.And.WithinTimeBudget(TimeSpan)` chain extension that composes with any behavioral assertion. For code that schedules timers, `ObservableTimeProvider` and `ObservableFakeTimeProvider` observe every timer it creates: assert on leaks, fires, and schedules, and wait for a timer change instead of sleeping in real time.

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
- [Compared with TUnit's `Within`, `CompletesWithin` and `WaitsFor`](#compared-with-tunits-within-completeswithin-and-waitsfor)
- [Entry points](#entry-points)
  - [`FakeTimeProvider` state assertions](#faketimeprovider-state-assertions)
  - [`TimeProvider`-aware `DateTimeOffset` assertions](#timeprovider-aware-datetimeoffset-assertions)
  - [Cross-cutting timing budget](#cross-cutting-timing-budget)
  - [Rate-limit assertions on invocation timestamps](#rate-limit-assertions-on-invocation-timestamps)
  - [Active-timer leak assertions](#active-timer-leak-assertions)
  - [Counting timer fires](#counting-timer-fires)
  - [Pending-timer due-time assertions](#pending-timer-due-time-assertions)
  - [Driving timer loops without real time](#driving-timer-loops-without-real-time)
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

**Requirements:** TUnit 1.84.0 or later, .NET 10. `TimeAssertions` (the framework-agnostic core) and `Microsoft.Extensions.TimeProvider.Testing` come transitively. The package is AOT-compatible, trimmable, and uses no runtime reflection in the assertion path.

## Package layout

This repo ships **two** NuGet packages:

| Package | Purpose | Depends on |
|---|---|---|
| [`TimeAssertions`](https://www.nuget.org/packages/TimeAssertions/) | Framework-agnostic core: `ObservableTimeProvider` (timer tracking and event-driven waits), `ObservableFakeTimeProvider` (an observed fake clock with advance helpers), `TimeRenderingHelpers` (failure-message formatting), `TimelineRenderer` | `Microsoft.Extensions.TimeProvider.Testing` |
| [`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/) | TUnit-specific entry points: `HasAdvancedExactly()`, `HasAdvancedApproximately()`, `HasUtcNow()`, `HasUtcNowApproximately()`, `IsRecent()`, `IsBeforeNow()`, `IsAfterNow()`, `WithinTimeBudget()`, `WithinTimeBudgetCapturing()`, `WasInvokedAtMostOncePer()`, `HasNoActiveTimers()`, `HasActiveTimerCount(int)`, `HasActiveTimers()`, `HasAtLeastActiveTimerCount(int)`, `HasNoActiveTimersEventually()`, `HasActiveTimerCountEventually()`, `HasAtLeastActiveTimerCountEventually()`, `HasAtMostActiveTimerCountEventually()`, `HasActiveTimersEventually()`, `HasNextTimerDueApproximately()`, `HasPendingTimerDueWithin()`, `HasTimerScheduled()`, `HasNoTimerScheduled()`, `HasTimerScheduledEventually()`, `HasTimerFiredCount(int)`, `HasNoTimerFired()`, `HasTimerFiredAtLeast(int)` | `TimeAssertions` + `TUnit.Assertions` + `TUnit.Core` + `Microsoft.Extensions.TimeProvider.Testing` |

You install `TimeAssertions.TUnit`; `TimeAssertions` and `Microsoft.Extensions.TimeProvider.Testing` come transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are not planned; the family targets TUnit.

## Namespaces (and a `GlobalUsings.cs` recommendation)

The two packages place types in two namespaces with deliberately-different scopes:

| Type / member | Namespace | Auto-imported? |
|---|---|---|
| `HasAdvancedExactly()`, `HasAdvancedApproximately()`, `HasUtcNow()`, `HasUtcNowApproximately()`, `IsRecent()`, `IsBeforeNow()`, `IsAfterNow()`, `WithinTimeBudget()`, `WithinTimeBudgetCapturing()`, `WasInvokedAtMostOncePer()`, `HasNoActiveTimers()`, `HasActiveTimerCount(int)`, `HasActiveTimers()`, `HasAtLeastActiveTimerCount(int)`, `HasNoActiveTimersEventually()`, `HasActiveTimerCountEventually()`, `HasAtLeastActiveTimerCountEventually()`, `HasAtMostActiveTimerCountEventually()`, `HasActiveTimersEventually()`, `HasNextTimerDueApproximately()`, `HasPendingTimerDueWithin()`, `HasTimerScheduled()`, `HasNoTimerScheduled()`, `HasTimerScheduledEventually()`, `HasTimerFiredCount(int)`, `HasNoTimerFired()`, `HasTimerFiredAtLeast(int)` (source-generated entries) | `TUnit.Assertions.Extensions` | **Yes**: TUnit auto-imports |
| `FakeTimeProvider` (the testable-clock type) | `Microsoft.Extensions.Time.Testing` | **No**: needed at the call site; recommended for `GlobalUsings.cs` |
| `ObservableTimeProvider`, `ObservableFakeTimeProvider` (timer observation and the observed fake clock), `TimeRenderingHelpers` (formatting utilities for failure messages) | `TimeAssertions` | **No**: needed at the call site; recommended for `GlobalUsings.cs` |
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
public async Task PreReleaseExpiration_advances_state_after_clock_moves_forward(CancellationToken cancellationToken)
{
    var fakeTime = new FakeTimeProvider();
    var service = new ExpirationService(fakeTime);   // defined in the cookbook below

    fakeTime.Advance(TimeSpan.FromMinutes(31));
    await service.ProcessAsync(cancellationToken);

    using (Assert.Multiple())
    {
        await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(31));
        await Assert.That(service.LastProcessedAt!.Value).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
        await Assert.That(service.NextRunAt).IsAfterNow(fakeTime);
    }
}
```

Every sample takes the test method's own `CancellationToken` parameter, which TUnit cancels when the test times out; `TestContext.Current!.Execution.CancellationToken` is for code that has no such parameter.

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

For projects standardizing on this pattern, TimeAssertions.TUnit is the TUnit-side test infrastructure that pays for itself test-by-test.

## Compared with TUnit's `Within`, `CompletesWithin` and `WaitsFor`

TUnit has its own time-related assertions. None of them takes a `TimeProvider`, so none can follow a fake clock:

| TUnit | What it does | Clock | Here instead |
|---|---|---|---|
| `IsEqualTo(x).Within(tolerance)` on a `TimeSpan`, `DateTime` or `DateTimeOffset` | Equality within a tolerance | None: it compares two values | `HasAdvancedApproximately`, `HasUtcNowApproximately` and `HasNextTimerDueApproximately` compare a fake clock's state within a tolerance |
| `CompletesWithin(timeout)` on a delegate | Races the delegate against `Task.Delay(timeout)` and fails with "it took too long to complete"; the delegate is not canceled | Real (`Stopwatch`, `CancellationTokenSource(timeout)`) | `WithinTimeBudget(budget)` waits for the evaluation, then compares the duration TUnit measured |
| `WaitsFor(...)` / `Eventually(...)` | Re-evaluates the source and polls, every 10 ms by default, until the timeout | Real (`Stopwatch`, `CancelAfter`) | The `*Eventually` timer assertions re-check on each timer change, with no polling; the [advance helpers](#driving-timer-loops-without-real-time) move a fake clock |
| `IsInPast()` / `IsInFuture()` | Compare with `DateTimeOffset.Now` | System | `IsBeforeNow(timeProvider)` / `IsAfterNow(timeProvider)` |

Use TUnit's for real-clock checks of real work, and these for code that takes a `TimeProvider`.

---

## Entry points

The entry points cover eight testing concerns: fake-clock state, `TimeProvider`-aware `DateTimeOffset` checks, assertion-level timing budgets, rate-limit assertions on invocation timestamps, timer-leak detection, timer fire counts, pending-timer due-time inspection, and driving timer loops without real time.

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

On failure these assertions say how far the clock advanced, or where it stands, in a culture-independent format: see [Failure diagnostics](#failure-diagnostics).

### `TimeProvider`-aware `DateTimeOffset` assertions

Distinct from TUnit core's `IsInPast()` / `IsInFuture()` (which always use the system clock):

| Entry point | Behavior |
|---|---|
| `IsRecent(TimeSpan window, TimeProvider? timeProvider = null)` | Asserts the timestamp is within the last `window` relative to the supplied `TimeProvider`'s notion of "now". When `timeProvider` is `null` or omitted, falls back to `TimeProvider.System` (useful for end-to-end tests not running under a fake clock). |
| `IsBeforeNow(TimeProvider timeProvider)` | Strict-before-now check against the supplied time provider. |
| `IsAfterNow(TimeProvider timeProvider)` | Strict-after-now check. |

```csharp
await Assert.That(service.LastProcessedAt!.Value).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
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

`.And.WithinTimeBudget()` is **post-facto**, not cancellation. The wall-clock duration is captured around the assertion's evaluation; the chain fails if the budget is exceeded but does NOT abort the assertion mid-flight. For a wait on background work, use a bounded wait instead: the `*Eventually` timer assertions here, or a sibling package's wait such as LogAssertions' `WaitForLogAsync`.

On a `Task<T>` source (`Assert.That(task)`) the direct form needs the type argument, `.WithinTimeBudget<int>(...)`, because TUnit's task source is an assertion source of both `T` and `Task<T>`; after `.And`, and on a value or delegate source, the type argument is inferred.

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
TestContext.Current!.OutputWriter.WriteLine($"asyncOp took {elapsed.TotalMilliseconds:F1}ms");
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
// The assertion takes an IReadOnlyList<DateTimeOffset>: declare the timestamps as one.
IReadOnlyList<DateTimeOffset> failureLogs = collector.GetSnapshot()
    .Where(r => r.Message.Contains("PingFailed", StringComparison.Ordinal))
    .Select(r => r.Timestamp)
    .ToList();

await Assert.That(failureLogs).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
```

The boundary is the one LogAssertions' `HasLogged()...AtMostOncePer(interval)` uses: a gap exactly `interval` long passes, one tick less fails. Two differences remain: this assertion takes the timestamps in the order given (an earlier timestamp after a later one fails at any interval), and it accepts an `interval` of zero; LogAssertions orders the records by timestamp and needs a positive interval.

The receiver is the recorded log itself, NOT the action being invoked: the consumer's production code calls the rate-limited operation, the test records each invocation's timestamp, and the assertion examines the recording. Caller is responsible for chronological order; the assertion preserves input order verbatim.

### Active-timer leak assertions

`HasNoActiveTimers()` and `HasActiveTimerCount(int)` assert on timer disposal: did a `BackgroundService` / `IHostedService` dispose every `ITimer` it started? `FakeTimeProvider` does not surface the timers created against it ([dotnet/extensions#7515](https://github.com/dotnet/extensions/issues/7515)), so wrap it in the framework-agnostic `ObservableTimeProvider` (shipped in the `TimeAssertions` core package), run the code under test, then assert.

| Entry point | Behavior |
|---|---|
| `HasNoActiveTimers()` | Asserts every timer created through the `ObservableTimeProvider` has been disposed. On failure the message names each survivor by its schedule (`[dueTime=..., period=...]`; one-shot timers as `period=one-shot`) with a grep-friendly `(count=N)` trailer, instead of a bare integer. |
| `HasActiveTimerCount(int expected)` | Asserts the exact number of active (undisposed) timers: the registration half of a disposal test. On mismatch the message renders expected vs actual counts plus each active timer's schedule, with an `(expected=N, actual=M)` trailer. |
| `HasActiveTimers()` | Asserts at least one timer is active: the positive-presence counterpart for the registration half of a disposal test, without pinning the exact count. |
| `HasAtLeastActiveTimerCount(int count)` | Asserts the active count is at least `count`, for a lower bound rather than an exact count. On a shortfall the message renders the required minimum vs actual count plus each active timer's schedule, with a `(minimum=N, actual=M)` trailer. |
| `HasNoActiveTimersEventually(cancellationToken)` or `(timeout, cancellationToken)` | Waits until no timer is active: the asynchronous disposal race a synchronous check cannot see (see below). Event-driven: the count is re-checked each time a timer is created, changed, fired, or disposed. On a timeout the message names each survivor by its schedule with a `(count=N)` trailer. |
| `HasActiveTimerCountEventually(int count, ...)` | The count-targeted sibling: waits until the active count equals `count`. On a timeout the message renders the expected and actual counts with an `(expected=N, actual=M)` trailer. |
| `HasAtLeastActiveTimerCountEventually(int count, ...)` | The asynchronous lower bound: waits until at least `count` timers are active. The right shape for a registration wait where more than one timer may register. On a timeout the message renders a `(minimum=N, actual=M)` trailer. |
| `HasAtMostActiveTimerCountEventually(int count, ...)` | The asynchronous upper bound: waits until at most `count` timers are active, for "the active set settles to no more than N". On a timeout the message renders a `(maximum=N, actual=M)` trailer. `HasAtMostActiveTimerCountEventually(0, ...)` is equivalent to `HasNoActiveTimersEventually`. |
| `HasActiveTimersEventually(...)` | The asynchronous counterpart of `HasActiveTimers()`: waits until at least one timer is active. A named shorthand for `HasAtLeastActiveTimerCountEventually(1, ...)`. |

```csharp
var time = new ObservableTimeProvider(new FakeTimeProvider());
var service = new HeartbeatService(time);

await service.StartAsync(cancellationToken);
await Assert.That(time).HasActiveTimerCount(1);   // the heartbeat timer registered

await service.StopAsync(cancellationToken);
await Assert.That(time).HasNoActiveTimers();       // ...and was disposed on stop
```

An `IHostedService` commonly disposes its timer on a continuation that runs *after* `StopAsync` returns to the caller, so a synchronous `HasNoActiveTimers()` just after stop can still see the timer. `HasNoActiveTimersEventually` handles that race by waiting for the disposal itself:

```csharp
await service.StopAsync(cancellationToken);
await Assert.That(time).HasNoActiveTimersEventually(cancellationToken);                           // bounded by the test's token
await Assert.That(time).HasNoActiveTimersEventually(TimeSpan.FromSeconds(2), cancellationToken);  // or fail after 2 s of real time
```

The wait neither polls nor advances the fake clock: the disposal wakes it. It returns at once when the timer is already gone (a token that is already canceled throws first), and otherwise as soon as the continuation disposes it. The `timeout` is a real-time bound for a disposal that never comes. With the token-only form the test's own timeout ends the wait: the test is recorded as canceled, and the cancellation still names the surviving timers (`Canceled while waiting for no active timers: 1 remained (count=1):` and the schedules). Pass a `timeout` shorter than the test's to fail sooner. The count-targeted siblings wait the same way for an exact count, a lower bound, an upper bound, and at least one active timer. The `pollingInterval` argument of versions before 0.10.0 was removed in 0.11.0.

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
await service.StartAsync(cancellationToken);

await Assert.That(time).HasNoTimerFired();             // scheduled, not yet fired
fakeTime.Advance(TimeSpan.FromSeconds(3));             // advance the inner fake clock
await Assert.That(time).HasTimerFiredCount(3);         // the loop ran three times
```

Advance fake time on the inner `FakeTimeProvider` you wrapped (keep a reference to it, as above), or use one `ObservableFakeTimeProvider` and advance that (see [Driving timer loops without real time](#driving-timer-loops-without-real-time)); `ObservableTimeProvider` forwards every clock operation to the inner provider. Once a periodic timer fires, `NextTimerDueTime` (and the `HasNextTimerDueApproximately` / `HasPendingTimerDueWithin` assertions) report the timer's period, since that is when the next callback is due. A one-shot timer that has fired is disabled and drops out of the pending-due calculation.

### Pending-timer due-time assertions

These assertions inspect the schedule pending timers carry on an `ObservableTimeProvider` **without advancing the clock**, so a test can verify which delay a loop just scheduled (for example a step of an exponential backoff, or an auto-close due in 30 s) rather than advancing fake time and inferring the delay from when the callback fires. `HasNextTimerDueApproximately` and `HasPendingTimerDueWithin` look at the "next" timer, the one with the smallest due time among the enabled (non-infinite) active timers; the underlying `ObservableTimeProvider.NextTimerDueTime` property exposes that value (or `null` when no enabled timer is pending). `HasTimerScheduled` and its siblings find a timer by its due time among any number of timers. A timer carries the due time it was created or last changed with; after a fire, a periodic timer carries its period and a one-shot timer none.

| Entry point | Behavior |
|---|---|
| `HasNextTimerDueApproximately(TimeSpan expected, TimeSpan tolerance)` | Asserts the next pending timer's due time is within `tolerance` of `expected`. On failure the message names the expected and observed due times and the delta, with a grep-friendly `(expected=Xms, tolerance=Yms, actual=Zms, delta=Wms)` trailer, or `actual=none` when no enabled timer is pending. |
| `HasPendingTimerDueWithin(TimeSpan min, TimeSpan max)` | Asserts the next pending timer's due time falls within the inclusive range `[min, max]`. On failure the message names the range and observed due time, with a `(min=Xms, max=Yms, actual=Zms)` trailer, or `actual=none` when no enabled timer is pending. |
| `HasTimerScheduled(TimeSpan dueTime, int atLeast = 1)` | Asserts that at least `atLeast` active timers carry `dueTime` (compared exactly, as the code under test passed it), whatever else is pending: "the auto-close is due in 30 s" while a heartbeat runs every second. On a shortfall the message lists every active timer's schedule with a `(minimum=N, actual=M)` trailer. |
| `HasNoTimerScheduled()` | Asserts that no active timer has a pending due time. An undisposed timer that is disabled (a fired one-shot timer, or one changed to `Timeout.InfiniteTimeSpan`) passes; `HasNoActiveTimers()` would fail on it. |
| `HasTimerScheduledEventually(TimeSpan dueTime, [int atLeast,] [TimeSpan timeout,] CancellationToken)` | Waits, event-driven, until enough timers carry `dueTime`: "both ping loops armed their 30 s delay" while a start-up gate and an hourly refresh are pending too. Bounded by the token, or by `timeout` of real time. |

```csharp
var time = new ObservableTimeProvider(new FakeTimeProvider());
var client = new ReconnectingClient(time);

await client.OnDisconnect();   // first reconnect attempt scheduled
// Assert the scheduled backoff delay directly, without advancing the clock:
await Assert.That(time).HasNextTimerDueApproximately(
    TimeSpan.FromMilliseconds(500), tolerance: TimeSpan.FromMilliseconds(1));

// Assert the intent (a close is due in 30 s) among other pending timers:
await Assert.That(time).HasTimerScheduled(TimeSpan.FromSeconds(30));
```

### Driving timer loops without real time

A `BackgroundService` that waits with `Task.Delay(delay, timeProvider, ct)`, `new CancellationTokenSource(timeout, timeProvider)`, or `WaitAsync(timeout, timeProvider, ct)` creates one timer per wait, usually on a thread-pool thread. After `fakeTime.Advance(...)` the woken loop runs on, and the next assertion races it. `ObservableFakeTimeProvider` closes that race without a real-time sleep. It is an `ObservableTimeProvider` over its own `FakeTimeProvider`, so a test injects one object, advances it, and asserts on it.

Which helper when:

| The test wants to | Use | Because |
|---|---|---|
| Advance a known amount and let every woken loop finish its step | `AdvanceAndSettleAsync(delta, ct)` | It waits until each woken loop has armed its next wait. |
| Drive an operation through an unknown number of waits until it completes (a reconnect backoff, a start that returns once connected) | `AdvanceOnEachTimerUntilAsync(task, step, ct)` | It advances once per new timer until the task completes, then reports the task's outcome. |
| Drive a loop on a periodic timer (`PeriodicTimer`, a periodic `ITimer`) | `AdvanceUntilAsync(step, until, maxAdvance, ct)` with a condition the ticks themselves satisfy, such as `TimerFireCount` | A periodic timer is not waited for; it steps until the condition holds, bounded in fake time. |
| Advance until a log line is written | `AdvanceOnEachTimerUntilAsync(collector.WaitForLogAsync(...), step, ct)` | The record is an event; the clock stops when it arrives. |
| Wait until a loop has reached its first wait, before advancing | `WaitForTimersCreatedAsync(count, ct)`, or `HasTimerScheduledEventually(dueTime, ct)` when other code also creates timers | An advance before the timer exists is lost. |
| Wait until a stopped loop's timers are gone | `HasNoActiveTimersEventually(ct)`, or `WaitForTimersDisposedAsync(count, ct)` | The disposal often runs on a continuation, after the stop returns. |
| Wait for any other condition on the provider | `WaitUntilAsync(condition, ct)` | It re-checks on each timer change. |

| Member | Behavior |
|---|---|
| `Advance(TimeSpan)`, `SetUtcNow(DateTimeOffset)` | Move the owned fake clock, firing every timer that becomes due. `Advance` and the advance helpers below throw `ArgumentOutOfRangeException` naming `delta` or `step` for a negative advance or one past `DateTimeOffset.MaxValue`, before that advance moves the clock. While `Fake.AutoAdvanceAmount` is set, the check against the latest time is left to the fake clock, because reading the clock would then move it. |
| `AdvanceAndSettleAsync(TimeSpan delta, CancellationToken)` | Advances, then waits until every loop woken by the advance has armed its next wait (as many timers created with a due time or re-armed with `Change` as one-shot timers fired; a timer created disabled counts once it is armed). The next assertion sees the loop's step completed. |
| `AdvanceOnEachTimerUntilAsync(Task until, TimeSpan step, CancellationToken)` | Advances by `step` each time a timer has been created since the previous advance (a timer created before the call counts for the first one), until `until` completes, and then completes as `until` did: a fault or a cancellation of `until` is rethrown. Drives a reconnect backoff through any number of failed attempts. It counts every timer created on the provider, so use it on a provider the loop has to itself; and `step` must cover the loop's longest single wait, or the next timer is never created and the helper waits until the token is canceled. |
| `AdvanceOnEachTimerUntilAsync<T>(Task<T> until, TimeSpan step, CancellationToken)` | The same for a task with a result, which it returns: `FakeLogRecord record = await time.AdvanceOnEachTimerUntilAsync(collector.WaitForLogAsync(filter, cancellationToken), step, cancellationToken);` |
| `AdvanceUntilAsync(TimeSpan step, Func<bool> until, TimeSpan maxAdvance, CancellationToken)` | Advances in settled steps until `until` holds, for loops on a periodic timer; throws `TimeoutException` after `maxAdvance` of fake time. `until` runs right after each step, and a periodic timer gives no sign that its loop is waiting again: a loop that awaits other work after each tick falls behind, and a `PeriodicTimer` that is not being awaited merges the ticks it missed. So base `until` on what the ticks themselves do, such as `TimerFireCount`, or wait for the loop's own signal after each `Advance`. |
| `Fake` | The owned `FakeTimeProvider`, for its other settings (`AutoAdvanceAmount`, `SetLocalTimeZone`). Inject the `ObservableFakeTimeProvider`, not `Fake`: timers created on `Fake` directly are not observed. |

```csharp
[Test]
public async Task Heartbeat_beats_once_per_interval_and_stops_cleanly(CancellationToken cancellationToken)
{
    var time = new ObservableFakeTimeProvider();
    var service = new HeartbeatService(time);   // loops on await Task.Delay(1 s, timeProvider, ct)
    await service.StartAsync(cancellationToken);
    await Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(1), cancellationToken);

    await time.AdvanceAndSettleAsync(TimeSpan.FromSeconds(1), cancellationToken);
    await time.AdvanceAndSettleAsync(TimeSpan.FromSeconds(1), cancellationToken);
    await Assert.That(service.Beats).IsEqualTo(2);   // both steps completed, no sleep

    await service.StopAsync(cancellationToken);
    await Assert.That(time).HasNoActiveTimersEventually(cancellationToken);
}
```

`AdvanceAndSettleAsync` assumes that each woken loop arms its next wait, by creating a timer (`Task.Delay`, `new CancellationTokenSource(timeout, timeProvider)`) or by re-arming one with `Change`: a callback that re-arms its own fired one-shot timer, or a timer created disabled. Moving a timer that is still pending is not an arm, so a loop that extends a watchdog deadline with `CancelAfter` on each iteration is waited for until it has armed its next wait. A loop that ends after its timer fired arms nothing, so the wait runs until the token is canceled, and the exception then says how far the clock moved, how many timers fired and how many were armed. A periodic timer keeps its schedule and is not waited for; use `AdvanceUntilAsync` for that.

The settle counts every timer the provider sees, not only those of the woken loops: when other code creates or re-arms timers on the same provider during the settle, it can return before a woken loop has finished its step. Use it when the loops you advance are the only code arming timers on the provider; otherwise wait for the loop's own outcome, for example `WaitUntilAsync` on its state.

The waits behind these helpers are public on every `ObservableTimeProvider`: `WaitForTimersCreatedAsync(count, cancellationToken)`, `WaitForTimersDisposedAsync(count, cancellationToken)`, and `WaitUntilAsync(condition, cancellationToken)` re-check on each timer change, with the token as the only bound. `TimersCreatedCount` and `TimersDisposedCount` never go down, unlike `ActiveTimerCount`, so, when the loop is the only code creating timers on the provider, "the loop armed its second backoff" is `WaitForTimersCreatedAsync(2, cancellationToken)`. With other timer users on the same provider, wait for something only the loop does, such as its backoff's due time with `HasTimerScheduledEventually(dueTime, cancellationToken)`. A creation is signaled only once the inner provider has scheduled the timer, and a schedule change only once the inner timer carries it, so an advance right after the wait is never too early for that timer. The wait confirms the creation or change, not that the timer is still pending: a one-shot timer that fired as it was created (a zero due time), or a disabled timer, does not fire on the advance; a periodic timer that fired as it was created fires again after its period.

#### Advancing until a task completes

`AdvanceOnEachTimerUntilAsync` observes the task it advances for, so one call is enough; there is no second `await` of the task afterwards.

```csharp
[Test]
public async Task Connect_retries_with_backoff_until_connected(CancellationToken cancellationToken)
{
    var time = new ObservableFakeTimeProvider();
    var client = new ReconnectingClient(time);   // waits Task.Delay(backoff, timeProvider, ct) between attempts

    await time.AdvanceOnEachTimerUntilAsync(client.ConnectAsync(cancellationToken), TimeSpan.FromSeconds(30), cancellationToken);

    await Assert.That(client.IsConnected).IsTrue();
}
```

When the task faults, the call throws the task's exception; when it is canceled, its cancellation. A test that expects the fault asserts on the call:

```csharp
await Assert.That(() => time.AdvanceOnEachTimerUntilAsync(client.ConnectAsync(cancellationToken), step, cancellationToken))
    .Throws<GiveUpException>();
```

Start the operation inside the lambda, as here, if your build enables `VSTHRD003` (Microsoft.VisualStudio.Threading.Analyzers): a lambda that returns a task started before it works the same way, but that rule reports it.

#### Advancing until a log line

`AdvanceUntilAsync` needs a condition it can check at once, and a log line is an event. With LogAssertions, pass the wait for the record; the `Task<T>` form returns the record:

```csharp
FakeLogRecord record = await time.AdvanceOnEachTimerUntilAsync(
    collector.WaitForLogAsync(LogFilter.Containing("connected", StringComparison.Ordinal), cancellationToken),
    TimeSpan.FromMilliseconds(50),
    cancellationToken);
```

The clock moves only while the record has not been logged, and stops as soon as it is. If the record never comes, the token ends the wait.

An assertion's task works the same way, for example LogAssertions' `WillLog(...)` from 0.14.0. Put the chain in a local async function that awaits it and pass that function's task: TUnit's analyzer (`TUnitAssertions0002`, an error) requires every `Assert.That(...)` to be awaited where it is written, so `Assert.That(...).AssertAsync()` passed as an argument does not build.

```csharp
await time.AdvanceOnEachTimerUntilAsync(ConnectedAsync(), TimeSpan.FromMilliseconds(50), cancellationToken);

async Task ConnectedAsync() =>
    await Assert.That(collector).WillLog(TimeSpan.FromSeconds(5), cancellationToken)
        .Containing("connected", StringComparison.Ordinal)
        .Once();
```

If the record does not come, the call fails with `WillLog`'s message, because `AdvanceOnEachTimerUntilAsync` observes the task.

#### When the token ends a wait

The token is the only bound of these waits. When it ends one, the `OperationCanceledException` says what the wait saw, so a test that its `[Timeout]` cancels still reports why; TUnit shows the message with the timeout:

```text
Waited for 3 timer(s) created, but 1 timer(s) were created before the wait was canceled.
```

```text
Advanced 3 time(s) by 00:00:01, 00:00:03 in total (clock now 2026-01-01T00:00:03.0000000+00:00), but the task had not completed when the wait was canceled: the next advance waited for timer 4, and 3 timer(s) were created. A loop in a wait longer than the step is not woken by an advance, so it creates no new timer.
```

```text
Advanced 1 time(s) by 00:00:01, 00:00:01 in total (clock now 2026-01-01T00:00:01.0000000+00:00): the last advance fired 1 one-shot timer(s), but only 0 timer(s) were armed (created with a due time, or re-armed with Change; moving a pending timer is not an arm) before the wait was canceled. A loop that ends after its timer fires arms no new one; for a periodic timer use AdvanceUntilAsync.
```

`WaitForTimersDisposedAsync` reports its count the same way, and `AdvanceUntilAsync` reports its advances in both its `TimeoutException` and its cancellation. `WaitUntilAsync` cannot describe an arbitrary condition, so its cancellation carries no description.

#### Bounding one wait

A lost timer otherwise surfaces only when the test's own timeout ends the test. To fail one wait sooner, pass a linked token with a limit:

```csharp
using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
bound.CancelAfter(TimeSpan.FromSeconds(5));
await time.WaitForTimersCreatedAsync(2, bound.Token);
```

The limit is real time and only a safety net. The wait itself ends on the event it waits for (a timer, or a log record), never on the wall clock, so a test that passes never waits for the limit. When the limit is reached, the test fails at once with the message above.

#### Waiting for pending timers

- **Right after an advance,** `AdvanceAndSettleAsync(delta, cancellationToken)` waits until the loops the advance woke have armed their next timers.
- **Until a number of timers are pending,** wait on the active timers' due state. A one-shot timer that fired and that its owner has not disposed stays active, so `ActiveTimerCount` still counts it, but it carries an infinite due time and is no longer pending:

  ```csharp
  await time.WaitUntilAsync(
      static provider => provider.ActiveTimers.Count(static timer => timer.DueTime != Timeout.InfiniteTimeSpan) >= 2,
      cancellationToken);
  ```

---

## Failure diagnostics

TUnit prints a failure as `Expected <what was expected>`, then `but <what was found>`, then the call. The `but` text of every assertion here says only what was found (`HasUtcNow` and `HasUtcNowApproximately` also name the expected time there, in the round-trip format, since TUnit would render it in the current culture), followed by a grep-friendly trailer (`(expected=N, actual=M)`, `(count=N)`, `(elapsed=…ms, budget=…ms, overrun=…ms)`) and, for the timer assertions, each timer's schedule. These texts are pinned by tests; the samples below are copied from them.

**A leak (`HasNoActiveTimers`):**

```text
Expected to have no active timers
but 2 remained (count=2):
  [dueTime=600ms, period=one-shot]
  [dueTime=30.0s, period=1.0s]

at Assert.That(time).HasNoActiveTimers()
```

**A due time (`HasNextTimerDueApproximately`):**

```text
Expected to have the next timer due in approximately 00:00:00.5000000 within tolerance 00:00:00.0010000
but it was due in 600ms, off by 100ms (expected=500ms, tolerance=1ms, actual=600ms, delta=100ms)

at Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1))
```

**An "eventually" wait that reached its bound (`HasActiveTimerCountEventually`):**

```text
Expected 3 active timer(s) within 50ms
but found 2 (expected=3, actual=2):
  [dueTime=600ms, period=one-shot]
  [dueTime=30.0s, period=1.0s]

at Assert.That(time).HasActiveTimerCountEventually(3, Bound, cancellationToken)
```

**The fake clock's own state (`HasAdvancedExactly`):** the times are culture-independent.

```text
Expected to have advanced by total 00:31:00
but it advanced by 00:30:00, off by 00:01:00 (start 2026-01-01T00:00:00.0000000+00:00, now 2026-01-01T00:30:00.0000000+00:00)

at Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(31))
```

**A rate limit (`WasInvokedAtMostOncePer`):**

```text
Expected to have consecutive timestamps at least 00:00:30 apart
but the gap at index 2 was 5.0s (minimum 30.0s)
  timestamps[1]: 2026-01-01T00:00:30.0000000+00:00
  timestamps[2]: 2026-01-01T00:00:35.0000000+00:00
  (gap=5000ms, minimum=30000ms)

at Assert.That(stamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30))
```

**A timing budget that was exceeded** (the durations are measured, so they vary per run):

```text
Expected to be 42
	and completion within timing budget of 1ms
but completed in 22ms: exceeded budget of 1ms by 21ms (elapsed=22ms, budget=1ms, overrun=21ms)

at Assert.That(slow).IsEqualTo(42).And.WithinTimeBudget(TimeSpan.FromMilliseconds(1))
```

**A source that threw.** After a behavioral assertion, that assertion reports it first:

```text
Expected to be 42
but threw InvalidOperationException

at Assert.That(refused).IsEqualTo(42).And.WithinTimeBudget(TimeSpan.FromMilliseconds(500))
```

With the budget first in the chain, the budget assertion reports it, with the exception's message:

```text
Expected completion within timing budget of 500ms
but threw InvalidOperationException: connection refused

at Assert.That(refused).WithinTimeBudget(TimeSpan.FromMilliseconds(500))
```

The cancellation messages of the waits are under [When the token ends a wait](#when-the-token-ends-a-wait).

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
public async Task PreReleaseExpiration_advances_state_after_clock_moves_forward(CancellationToken cancellationToken)
{
    var startedAt = new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero);
    var fakeTime = new FakeTimeProvider();
    fakeTime.SetUtcNow(startedAt);

    var service = new ExpirationService(fakeTime, ttl: TimeSpan.FromMinutes(30));

    fakeTime.Advance(TimeSpan.FromMinutes(31));
    await service.ProcessAsync(cancellationToken);

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

After `fakeTime.Advance(...)`, a woken loop continues on a thread-pool thread; it has not finished its step when `Advance` returns. The common workarounds wait in real time:

```csharp
// Patterns to avoid: real time on every run, and still a guess.
fakeTime.Advance(TimeSpan.FromMinutes(2));
await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);     // hope continuations have drained
await Assert.That(() => collector.Count)
    .Eventually(a => a.IsGreaterThanOrEqualTo(1), TimeSpan.FromSeconds(1));  // polls every 10 ms
```

When a timer drives the effect, wait for the timer instead (see [Driving timer loops without real time](#driving-timer-loops-without-real-time)):

```csharp
await time.AdvanceAndSettleAsync(TimeSpan.FromMinutes(2), cancellationToken);   // the woken loop armed its next timer
await Assert.That(collector).HasLogged().Containing("[Heartbeat]", StringComparison.Ordinal).Once();
```

When a log record, not a timer, marks the effect, advance until it is logged: see [Advancing until a log line](#advancing-until-a-log-line).

For timer state, replace TUnit's `Eventually` with the event-driven assertions: `Assert.That(() => time.ActiveTimerCount).Eventually(...)` becomes `HasActiveTimerCountEventually(count, cancellationToken)`, `HasNoActiveTimersEventually(cancellationToken)`, or `HasTimerScheduledEventually(dueTime, cancellationToken)`. They wake on the timer change itself, return at once when the state already holds, and name the timers on failure.

For a condition that no timer change signals (a counter updated by another thread, a value from an external system), TUnit's `Eventually` or its alias `WaitsFor` still fits. Pass the test's own token so that an external cancel (parent `[Timeout]`, runner cancel) ends the polling loop instead of waiting for the timeout argument:

```csharp
[Test]
public async Task Queue_drains_before_parent_timeout(CancellationToken cancellationToken)
{
    await Assert.That(() => queue.Count)
        .Eventually(a => a.IsEqualTo(0),
                    timeout: TimeSpan.FromSeconds(10),
                    cancellationToken: cancellationToken);
}
```

The `WithinTimeBudget` / `WithinTimeBudgetCapturing` chains in this package propagate `OperationCanceledException` intact since v0.5.0, so a chain like `Eventually(...).And.WithinTimeBudget(...)` surfaces cancellation as a canceled test rather than an assertion failure.

**Why real-time waits cost more than they look.** A 50 ms sleep is paid on every run, whether the continuation needed 1 ms or 40. Under mutation testing (for example Stryker.NET) the tests that cover a mutant run again for every mutant, so the sleep is multiplied by the mutant count: 300 mutants covered by ten tests with one such sleep each add 150 s of pure waiting to a run. An event-driven wait costs nothing when the state already holds.

### Keeping the test's own timeouts off the observed clock (MA0166)

With [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer), MA0166 asks for the `TimeProvider` overload of a call whenever a `TimeProvider` is in scope, for example `WaitAsync(timeout, timeProvider, ct)` instead of `WaitAsync(timeout, ct)`. In a test that holds an `ObservableTimeProvider`, following it for the test's own orchestration timeout has two effects. The timeout creates a timer on the observed provider, so the active and created counts are one higher than the code under test produced. And it runs on the fake clock: it never fires unless the test advances, and an advance meant for the code under test can fire it.

```csharp
// The test's safety timeout: satisfy MA0166 with the system clock, not the observed one.
await startup.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, cancellationToken);
```

Pass `TimeProvider.System` for orchestration timeouts, or bound the wait with the test's `CancellationToken` alone.

### Pinning the moment-graph of a multi-event sequence

When a test produces a sequence of named events at known fake-time moments, snapshot the whole graph rather than asserting on each event individually. `TimelineRenderer` produces a deterministic byte-stable string from a list of `(Timestamp, Label)` pairs; pair it with `MatchesSnapshot()` from `SnapshotAssertions.TUnit` to pin the graph against a committed baseline.

```csharp
using TimeAssertions.Render;

[Test]
public async Task HeartbeatService_emits_at_expected_cadence(CancellationToken cancellationToken)
{
    var epoch = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);
    var fakeTime = new FakeTimeProvider();
    fakeTime.SetUtcNow(epoch);
    var events = new List<TimelineEvent>();

    var service = new HeartbeatService(fakeTime, ev => events.Add(new TimelineEvent(fakeTime.GetUtcNow(), ev)));
    await service.StartAsync(cancellationToken);
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
    .ThrowsNothing()
    .And.WithinTimeBudgetCapturing(TimeSpan.FromSeconds(2), e => elapsed = e);

// Surface the measured latency in test output, even when the assertion passes.
TestContext.Current!.OutputWriter.WriteLine($"GET /health: {elapsed.TotalMilliseconds:F1}ms");
```

### Verifying a periodic-probe suppression window

A common production pattern: a background component periodically probes some external resource (HTTP health endpoint, message broker, database connectivity). When the probe fails, the first failure is logged; subsequent failures inside a suppression window are deliberately silenced so a sustained outage does not flood the log. The contract to verify is "the failure handler logs at most once per `<window>` seconds".

`WasInvokedAtMostOncePer` asserts the consecutive-pair gap against a minimum interval. Extract the failure timestamps from whatever recording mechanism the test uses (a `FakeLogCollector`, a captured event probe, a list populated in a wrapped callback) and assert against the recording.

```csharp
[Test]
public async Task PingHandler_suppresses_repeated_failures_within_30s_window(CancellationToken cancellationToken)
{
    var fakeTime = new FakeTimeProvider();
    // The collector stamps each record with the fake clock's time.
    var collector = FakeLogCollector.Create(new FakeLogCollectorOptions { TimeProvider = fakeTime });
    var handler = new PingHandler(fakeTime, new FakeLogger<PingHandler>(collector));

    // Simulate ten consecutive ping failures spaced 5s apart of fake time.
    for (var i = 0; i < 10; i++)
    {
        await handler.HandleFailureAsync(cancellationToken);
        fakeTime.Advance(TimeSpan.FromSeconds(5));
    }

    // Production code logs every failure as "[PingFailed]" but suppresses the second-and-later
    // occurrence within any 30-second window. After ten failures spaced 5s apart, we expect at
    // most one log entry per 30-second window: two failures total (at t=0 and t=30).
    IReadOnlyList<DateTimeOffset> failureTimestamps = collector.GetSnapshot()
        .Where(r => r.Message.Contains("[PingFailed]", StringComparison.Ordinal))
        .Select(r => r.Timestamp)
        .ToList();

    await Assert.That(failureTimestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
}
```

The assertion preserves input order. If the underlying mechanism does not guarantee chronological order (rare; log collectors usually do), sort before asserting. The failure names the first pair that is too close, the observed gap, and the required minimum, so a regression in the suppression window is immediately legible: see the rate-limit sample under [Failure diagnostics](#failure-diagnostics).

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

**Pattern B: warm-up call (precise).** Factor a single warm-up invocation before the measured call. The warm-up amortizes JIT and one-shot init; the measured call sees steady-state cost only.

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
TestContext.Current!.OutputWriter.WriteLine($"steady-state: {elapsed.TotalMilliseconds:F1}ms");
```

---

## Design notes

- **`WithinTimeBudget`, not `Within`.** TUnit core already uses `.Within(...)` for tolerance assertions, so a separate name avoids the overload collision and reads naturally after `.And`. It is generic in the source's value type: `.And.WithinTimeBudget(...)` and a value or delegate source infer it, while a `Task<T>` source (`Assert.That(task).WithinTimeBudget<int>(...)`) needs the type argument.
- **`IsBeforeNow` / `IsAfterNow`, not `IsInPast` / `IsInFuture`.** TUnit core's versions use the system clock; ours take a `TimeProvider`. Distinct names make the mechanism obvious: use TUnit's for system-clock tests, ours for `FakeTimeProvider`-driven ones.
- **`Microsoft.Extensions.TimeProvider.Testing` is propagated, not `PrivateAssets="all"`.** Consumers asserting on `FakeTimeProvider` need it in scope; making it transitive saves an explicit reference per test project. Since 0.10.0 the core depends on it too, because `ObservableFakeTimeProvider` owns a `FakeTimeProvider`. It is Microsoft's own MIT-licensed package from the same repository as the `TimeProvider` testing story.
- **Waits are event-driven; bounds are real time.** `ObservableTimeProvider` sees every timer creation, change, fire, and disposal, so each wait re-checks its condition on the change itself and nothing polls. A `timeout` bounds a wait in real time on purpose: the provider's own clock is usually a fake one that moves only when the test advances it.

## Stability intent (pre-1.0)

This is a 0.x release and the public API may evolve. Specifically:

- **Additive changes** (new entry points, new shorthand wrappers, new tolerance overloads) ship in any patch / minor without breaking ApiCompat.
- **Breaking changes** to existing signatures bump the minor version (0.X.0) and are called out in the [CHANGELOG](CHANGELOG.md).
- **`PackageValidationBaselineVersion`** is pinned to the previous shipped version starting from 0.1.1, so ApiCompat breakage is caught at pack time.

The 1.0 milestone signals API stability: see [Limitations and future work](#limitations-and-future-work) for what's still being designed.

## Limitations and future work

`WithinTimeBudget` uses TUnit's `EvaluationMetadata<T>.Duration`, which TUnit takes from `DateTimeOffset.Now` before and after the evaluation; system-clock jumps mid-test are vanishingly rare. A `Stopwatch.GetTimestamp()`-based variant is not planned: a chained assertion only sees the times TUnit recorded, and measuring with a monotonic clock would need a hook before the evaluation that TUnit does not give a chained assertion.

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

- **[`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/)**: fluent log assertions over `Microsoft.Extensions.Logging.Testing.FakeLogCollector`. Use `.And.WithinTimeBudget(...)` to add a timing budget to any `HasLogged()` chain, and `AdvanceOnEachTimerUntilAsync` with its `WaitForLogAsync` to drive a fake clock until a record is logged ([Advancing until a log line](#advancing-until-a-log-line)); neither package depends on the other.
- **[`SnapshotAssertions.TUnit`](https://www.nuget.org/packages/SnapshotAssertions.TUnit/)**: text-snapshot assertions for API-surface tests and similar deterministic-string scenarios. Coexists with Verify; covers the 80% case without coverage friction.
- **[`MathAssertions.TUnit`](https://www.nuget.org/packages/MathAssertions.TUnit/)**: tolerance-aware fluent assertions over numeric and geometric types (vectors, quaternions, matrices, planes, complex numbers, arrays).
- **[`JsonAssertions.TUnit`](https://www.nuget.org/packages/JsonAssertions.TUnit/)**: fluent JSON assertions over `System.Text.Json`, HTTP response bodies (including RFC 7807 ProblemDetails), and source-generated `JsonSerializerContext` registration.
- **[`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/)**: Server-Sent Events (SSE) wire-format assertions: event-count, field shape (`event:`, `data:`, `id:`, `retry:`), and stream content validation.
- **[`GrpcAssertions.TUnit`](https://www.nuget.org/packages/GrpcAssertions.TUnit/)**: fluent gRPC outcome assertions (`ThrowsGrpcException` with `StatusCode` shorthands and detail refinements) plus the `GrpcCallBuilder` test-double helper.
- **[`TracingAssertions.TUnit`](https://www.nuget.org/packages/TracingAssertions.TUnit/)**: fluent OpenTelemetry distributed-tracing (`Activity` / span) assertions: operation name, tags, status, kind, events, and parent/child, root and same-trace relationships, captured via a raw `ActivityListener` with no OpenTelemetry SDK dependency.
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
