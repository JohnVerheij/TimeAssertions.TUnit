# TimeAssertions.TUnit

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

`TimeAssertions` (the framework-agnostic core) and `Microsoft.Extensions.TimeProvider.Testing` come transitively. **Requirements:** TUnit 1.45.0 or later, .NET 10.

The source-generated entry points (`HasAdvancedExactly`, `HasAdvancedApproximately`, `HasUtcNow`, `HasUtcNowApproximately`, `IsRecent`, `IsBeforeNow`, `IsAfterNow`, `WithinTimeBudget`, `WithinTimeBudgetCapturing`, `WasInvokedAtMostOncePer`) auto-import via `TUnit.Assertions.Extensions`. Add the following to a `GlobalUsings.cs` in your test project for the call-site and `FakeTimeProvider` namespaces:

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

    // Cross-cutting timing budget on any behavioural assertion chain
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
| `WithinTimeBudget(TimeSpan)` | Cross-cutting timing budget; chains via `.And` after any behavioural assertion |
| `WithinTimeBudgetCapturing(TimeSpan, Action<TimeSpan>)` | Same as `WithinTimeBudget` plus a callback that always receives the measured elapsed (added in v0.2.0) |
| `WasInvokedAtMostOncePer(this IReadOnlyList<DateTimeOffset>, TimeSpan interval)` | Rate-limit assertion on a recorded invocation log: every consecutive gap is at least `interval` (added in v0.5.0) |

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

## License

[MIT](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
