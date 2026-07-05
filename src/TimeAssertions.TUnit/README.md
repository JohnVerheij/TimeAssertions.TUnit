# TimeAssertions.TUnit

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family of assertion extensions for TUnit.


[![NuGet](https://img.shields.io/nuget/v/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/TimeAssertions.TUnit.svg)](https://www.nuget.org/packages/TimeAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

TUnit-native fluent time-assertion DSL on top of `Microsoft.Extensions.Time.Testing.FakeTimeProvider`. Adds `FakeTimeProvider` state assertions, `TimeProvider`-aware `DateTimeOffset` recency / past / future checks, plus the cross-cutting `.WithinTimeBudget(TimeSpan)` chain extension. AOT-compatible, trimmable, no reflection.

> **Full documentation, "Why TimeProvider in tests", cookbook, design notes, and roadmap:** [github.com/JohnVerheij/TimeAssertions.TUnit](https://github.com/JohnVerheij/TimeAssertions.TUnit)

## Install

```bash
dotnet add package TimeAssertions.TUnit
```

`TimeAssertions` (the framework-agnostic core) and `Microsoft.Extensions.TimeProvider.Testing` come transitively. **Requirements:** TUnit 1.58.0 or later, .NET 10.

The source-generated entry points (`HasAdvancedExactly`, `HasAdvancedApproximately`, `HasUtcNow`, `HasUtcNowApproximately`, `IsRecent`, `IsBeforeNow`, `IsAfterNow`, `WithinTimeBudget`, `WithinTimeBudgetCapturing`, `WasInvokedAtMostOncePer`, `HasNoActiveTimers`, `HasActiveTimerCount`, `HasActiveTimers`, `HasAtLeastActiveTimerCount`, `HasNoActiveTimersEventually`, `HasActiveTimerCountEventually`, `HasAtLeastActiveTimerCountEventually`, `HasAtMostActiveTimerCountEventually`, `HasActiveTimersEventually`, `HasNextTimerDueApproximately`, `HasPendingTimerDueWithin`, `HasTimerFiredCount`, `HasNoTimerFired`, `HasTimerFiredAtLeast`) auto-import via `TUnit.Assertions.Extensions`. Add the following to a `GlobalUsings.cs` in your test project for the call-site and `FakeTimeProvider` namespaces:

```csharp
global using Microsoft.Extensions.Time.Testing;
global using TimeAssertions;
global using TimeAssertions.TUnit;
```

## Quick start

```csharp
[Test]
public async Task PreReleaseExpiration_advances_state_after_clock_moves_forward()
{
    var fakeTime = new FakeTimeProvider();
    var service = new ExpirationService(fakeTime);

    fakeTime.Advance(TimeSpan.FromMinutes(31));

    await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(31));
    await Assert.That(service.LastRefresh).IsRecent(TimeSpan.FromSeconds(1), fakeTime);

    // Cross-cutting timing budget on any behavioral assertion chain
    await Assert.That(service.IsExpiredAsync())
        .IsTrue()
        .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
}
```

## Entry points

| Method | Purpose |
|---|---|
| `HasAdvancedExactly(TimeSpan)` / `HasAdvancedApproximately(total, tolerance)` | `FakeTimeProvider` advanced by exact / approximate amount (renamed from `HasAdvanced` / `HasAdvancedBy` in v0.2.0; old names `[Obsolete]` until v0.4.0) |
| `HasUtcNow(DateTimeOffset)` / `HasUtcNowApproximately(expected, tolerance)` | `FakeTimeProvider` is at exact / approximate moment |
| `IsRecent(TimeSpan, TimeProvider?)` | `DateTimeOffset` is within window before "now" of supplied (or system) clock |
| `IsBeforeNow(TimeProvider)` / `IsAfterNow(TimeProvider)` | `DateTimeOffset` ordering relative to supplied clock |
| `WithinTimeBudget(TimeSpan)` | Cross-cutting timing budget; chains via `.And` after any behavioral assertion |
| `WithinTimeBudgetCapturing(TimeSpan, Action<TimeSpan>)` | Same as `WithinTimeBudget` plus a callback that receives the measured elapsed on every evaluation path except external cancellation (added in v0.2.0; cancellation-skip behavior added in v0.5.0) |
| `WasInvokedAtMostOncePer(this IReadOnlyList<DateTimeOffset>, TimeSpan interval)` | Rate-limit assertion on a recorded invocation log: every consecutive gap is at least `interval` (added in v0.5.0) |
| `HasNoActiveTimers()` / `HasActiveTimerCount(int)` on `ObservableTimeProvider` | Timer-leak assertions: no undisposed timers / exact active-timer count, naming each survivor by its schedule on failure (added in v0.6.0) |
| `HasActiveTimers()` / `HasAtLeastActiveTimerCount(int)` on `ObservableTimeProvider` | Positive-count assertions: at least one / at least `count` active timers, for a lower bound rather than an exact count (added in v0.7.0) |
| `HasNoActiveTimersEventually(TimeSpan, ...)` / `HasActiveTimerCountEventually(int, TimeSpan, ...)` on `ObservableTimeProvider` | Real-time poll until the active count reaches zero / a target count, for the asynchronous disposal race a synchronous check cannot see (added in v0.7.0) |
| `HasNextTimerDueApproximately(TimeSpan, TimeSpan)` / `HasPendingTimerDueWithin(TimeSpan, TimeSpan)` on `ObservableTimeProvider` | Pending-timer due-time assertions: inspect the next scheduled timer's due time without advancing the clock, within a tolerance or an inclusive range (added in v0.6.0) |
| `HasTimerFiredCount(int)` / `HasNoTimerFired()` / `HasTimerFiredAtLeast(int)` on `ObservableTimeProvider` | Timer-fire assertions: how many times timer callbacks ran in total, cumulative and surviving disposal (added in v0.9.0) |

## Failure diagnostics

On a failed assertion, the exception message includes the elapsed / expected duration, the absolute drift, and (for budget overruns) the overshoot plus a grep-friendly `(elapsed=Xms, budget=Yms, overrun=Zms)` suffix for log scrapers. No `Console.WriteLine` debugging needed: every dimension you can assert on is also rendered in the failure message.

[Full failure-diagnostics example, design notes, stability intent, and roadmap on GitHub.](https://github.com/JohnVerheij/TimeAssertions.TUnit#failure-diagnostics)

## Family

Part of an assertion family for TUnit:

- [LogAssertions.TUnit](https://github.com/JohnVerheij/LogAssertions.TUnit)
- [SnapshotAssertions.TUnit](https://github.com/JohnVerheij/SnapshotAssertions.TUnit)
- [MathAssertions.TUnit](https://github.com/JohnVerheij/MathAssertions.TUnit)
- [JsonAssertions.TUnit](https://github.com/JohnVerheij/JsonAssertions.TUnit)
- [SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)
- [GrpcAssertions.TUnit](https://github.com/JohnVerheij/GrpcAssertions.TUnit)

## License

[MIT](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
