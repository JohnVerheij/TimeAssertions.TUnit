# TimeAssertions.TUnit

[![NuGet](https://img.shields.io/nuget/v/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0%20%7C%2011.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet)

> **Scope:** Test projects only. Not intended for production code.

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family of assertion extensions for TUnit.

TUnit-native fluent time-assertion DSL on top of `Microsoft.Extensions.Time.Testing.FakeTimeProvider`. Adds `FakeTimeProvider` state assertions, `TimeProvider`-aware `DateTimeOffset` recency / past / future checks, plus the cross-cutting `.WithinTimeBudget(TimeSpan)` chain extension, and timer assertions on an `ObservableTimeProvider`: leaks, fires, schedules, and event-driven waits that react to each timer change instead of polling. AOT-compatible, trimmable, no reflection.

> **Full documentation, "Why TimeProvider in tests", cookbook, design notes, and roadmap:** [timeassertions.dev](https://timeassertions.dev) (source: [github.com/JohnVerheij/TimeAssertions.TUnit](https://github.com/JohnVerheij/TimeAssertions.TUnit))

## Install

```bash
dotnet add package TimeAssertions.TUnit
```

`TimeAssertions` (the framework-agnostic core) and `Microsoft.Extensions.TimeProvider.Testing` come transitively. **Requirements:** TUnit 1.84.0 or later, .NET 10 or .NET 11.

The source-generated entry points (`HasAdvancedExactly`, `HasAdvancedApproximately`, `HasUtcNow`, `HasUtcNowApproximately`, `IsRecent`, `IsBeforeNow`, `IsAfterNow`, `WithinTimeBudget`, `WithinTimeBudgetCapturing`, `WasInvokedAtMostOncePer`, `HasNoActiveTimers`, `HasActiveTimerCount`, `HasActiveTimers`, `HasAtLeastActiveTimerCount`, `HasNoActiveTimersEventually`, `HasActiveTimerCountEventually`, `HasAtLeastActiveTimerCountEventually`, `HasAtMostActiveTimerCountEventually`, `HasActiveTimersEventually`, `HasNextTimerDueApproximately`, `HasPendingTimerDueWithin`, `HasTimerScheduled`, `HasNoTimerScheduled`, `HasTimerScheduledEventually`, `HasTimerFiredCount`, `HasNoTimerFired`, `HasTimerFiredAtLeast`) auto-import via `TUnit.Assertions.Extensions`. Add the following to a `GlobalUsings.cs` in your test project for the call-site and `FakeTimeProvider` namespaces:

```csharp
global using Microsoft.Extensions.Time.Testing;
global using TimeAssertions;
global using TimeAssertions.TUnit;
```

## Quick start

```csharp
[Test]
public async Task PreReleaseExpiration_advances_state_after_clock_moves_forward(CancellationToken cancellationToken)
{
    var fakeTime = new FakeTimeProvider();
    var service = new ExpirationService(fakeTime);

    fakeTime.Advance(TimeSpan.FromMinutes(31));
    await service.ProcessAsync(cancellationToken);

    await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(31));
    await Assert.That(service.LastProcessedAt!.Value).IsRecent(TimeSpan.FromSeconds(1), fakeTime);

    // Cross-cutting timing budget on any behavioral assertion chain
    await Assert.That(() => service.NextRunAt)
        .IsAfterNow(fakeTime)
        .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
}
```

Samples take the test method's own `CancellationToken` parameter; `TestContext.Current!.Execution.CancellationToken` is for code that has none.

## Entry points

| Method | Purpose |
|---|---|
| `HasAdvancedExactly(TimeSpan)` / `HasAdvancedApproximately(total, tolerance)` | `FakeTimeProvider` advanced by exact / approximate amount; a failure says how far it advanced |
| `HasUtcNow(DateTimeOffset)` / `HasUtcNowApproximately(expected, tolerance)` | `FakeTimeProvider` is at exact / approximate moment |
| `IsRecent(TimeSpan, TimeProvider?)` | `DateTimeOffset` is within window before "now" of supplied (or system) clock |
| `IsBeforeNow(TimeProvider)` / `IsAfterNow(TimeProvider)` | `DateTimeOffset` ordering relative to supplied clock |
| `WithinTimeBudget(TimeSpan)` | Cross-cutting timing budget; chains via `.And` after any behavioral assertion |
| `WithinTimeBudgetCapturing(TimeSpan, Action<TimeSpan>)` | Same as `WithinTimeBudget` plus a callback that receives the measured elapsed on every evaluation path except external cancellation (added in v0.2.0; cancellation-skip behavior added in v0.5.0) |
| `WasInvokedAtMostOncePer(this IReadOnlyList<DateTimeOffset>, TimeSpan interval)` | Rate-limit assertion on a recorded invocation log: every consecutive gap is at least `interval` (added in v0.5.0) |
| `HasNoActiveTimers()` / `HasActiveTimerCount(int)` on `ObservableTimeProvider` | Timer-leak assertions: no undisposed timers / exact active-timer count, naming each survivor by its schedule on failure (added in v0.6.0) |
| `HasActiveTimers()` / `HasAtLeastActiveTimerCount(int)` on `ObservableTimeProvider` | Positive-count assertions: at least one / at least `count` active timers, for a lower bound rather than an exact count (added in v0.7.0) |
| `HasNoActiveTimersEventually(...)` / `HasActiveTimerCountEventually(int, ...)` on `ObservableTimeProvider` | Wait until the active count reaches zero / a target count, for the asynchronous disposal race a synchronous check cannot see (added in v0.7.0; event-driven since v0.10.0: the count is re-checked on each timer change, bounded by the token or a real-time `timeout`; since v0.11.0 a canceled wait names the timers) |
| `HasAtLeastActiveTimerCountEventually(int, ...)` / `HasAtMostActiveTimerCountEventually(int, ...)` / `HasActiveTimersEventually(...)` on `ObservableTimeProvider` | Wait for a lower bound / an upper bound / at least one active timer (added in v0.8.0; event-driven since v0.10.0) |
| `HasNextTimerDueApproximately(TimeSpan, TimeSpan)` / `HasPendingTimerDueWithin(TimeSpan, TimeSpan)` on `ObservableTimeProvider` | Pending-timer due-time assertions: inspect the next scheduled timer's due time without advancing the clock, within a tolerance or an inclusive range (added in v0.6.0) |
| `HasTimerFiredCount(int)` / `HasNoTimerFired()` / `HasTimerFiredAtLeast(int)` on `ObservableTimeProvider` | Timer-fire assertions: how many times timer callbacks ran in total, cumulative and surviving disposal (added in v0.9.0) |
| `HasTimerScheduled(TimeSpan, int atLeast = 1)` / `HasNoTimerScheduled()` / `HasTimerScheduledEventually(TimeSpan, ...)` on `ObservableTimeProvider` | Due-time assertions: find a timer by the due time the code passed, among any number of timers; assert nothing is scheduled; wait until a timer (or `atLeast` timers) carries the due time (added in v0.10.0) |
| `ObservableFakeTimeProvider` (core) | One object to inject, advance, and assert on: `AdvanceAndSettleAsync`, `AdvanceOnEachTimerUntilAsync`, and `AdvanceUntilAsync` drive timer loops without real time (added in v0.10.0). Since v0.11.0 `AdvanceOnEachTimerUntilAsync` completes as its task did, rethrowing a fault, and its `Task<T>` form returns the result |

## Failure diagnostics

A failure says what was expected, then only what was found: the counts with a grep-friendly trailer (`(expected=N, actual=M)`, `(count=N)`, `(elapsed=Xms, budget=Yms, overrun=Zms)`), each timer's schedule for the timer assertions, and for the fake clock how far it advanced, in a culture-independent format. For example:

```text
Expected to have no active timers
but 2 remained (count=2):
  [dueTime=600ms, period=one-shot]
  [dueTime=30.0s, period=1.0s]

at Assert.That(time).HasNoActiveTimers()
```

When the test's token ends a wait, the cancellation says what the wait saw, for example `Waited for 3 timer(s) created, but 1 timer(s) were created before the wait was canceled.`

[Full failure-diagnostics example, design notes, stability intent, and roadmap on GitHub.](https://github.com/JohnVerheij/TimeAssertions.TUnit#failure-diagnostics)

## Family

Part of an assertion family for TUnit:

- [LogAssertions.TUnit](https://github.com/JohnVerheij/LogAssertions.TUnit)
- [SnapshotAssertions.TUnit](https://github.com/JohnVerheij/SnapshotAssertions.TUnit)
- [MathAssertions.TUnit](https://github.com/JohnVerheij/MathAssertions.TUnit)
- [JsonAssertions.TUnit](https://github.com/JohnVerheij/JsonAssertions.TUnit)
- [SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)
- [GrpcAssertions.TUnit](https://github.com/JohnVerheij/GrpcAssertions.TUnit)
- [TracingAssertions.TUnit](https://github.com/JohnVerheij/TracingAssertions.TUnit)
- [MetricsAssertions.TUnit](https://github.com/JohnVerheij/MetricsAssertions.TUnit)

## License

[MIT](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
