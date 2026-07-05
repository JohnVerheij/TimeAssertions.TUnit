# TimeAssertions

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family. This is the framework-agnostic core; the TUnit assertions live in the matching `.TUnit` package.


[![NuGet](https://img.shields.io/nuget/v/TimeAssertions.svg)](https://www.nuget.org/packages/TimeAssertions/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

Framework-agnostic rendering helpers and test infrastructure for the TimeAssertions package family. The actual `FakeTimeProvider` and `TimeProvider`-aware `DateTimeOffset` assertion APIs ship in the framework-specific adapter packages (currently `TimeAssertions.TUnit`).

> **Most users want [`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/), not this package directly.** This package only ships the shared rendering helpers; the adapter package adds the assertion entry points your test framework expects.

---

## What's in this package

- **`TimeRenderingHelpers`**: formatting utilities for elapsed durations and time budgets in failure-message context. Pure, allocation-conscious.
- **`ObservableTimeProvider`**: a `TimeProvider` decorator that tracks the timers created against it (`ActiveTimerCount`, `ActiveTimers`, `NextTimerDueTime`) and counts their callback fires (`TimerFireCount`, cumulative and surviving disposal) so adapter packages can assert on timer-disposal, leak, pending-due-time, and fire-count behavior without advancing the clock. Reflection-free, AOT-compatible, thread-safe.
- **`ActiveTimerInfo`**: a readonly record struct describing a tracked timer's schedule (`DueTime`, `Period`) and its fire count (`TimesFired`), returned by `ObservableTimeProvider.ActiveTimers`.
- **`TimelineRenderer`** / **`TimelineEvent`**: render a sequence of timestamped events as deterministic, snapshot-friendly text.

## Test-framework adapters

| Package | Test framework | Status |
|---|---|---|
| [`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/) | TUnit | Available now |
| `TimeAssertions.NUnit` | NUnit | Possible if there is demand |
| `TimeAssertions.xUnit` | xUnit | Possible if there is demand |
| `TimeAssertions.MSTest` | MSTest | Possible if there is demand |

If you'd find a non-TUnit adapter useful, [open a feature request](https://github.com/JohnVerheij/TimeAssertions.TUnit/issues/new?template=feature_request.yml): adapters are not built proactively.

## Installation

```bash
dotnet add package TimeAssertions.TUnit
```

`TimeAssertions` comes transitively. You don't need to install it directly unless you're building your own adapter package.

## Stability

The public surfaces above are semver-bound. Breaking changes require a major version bump. The exact text format of `TimeRenderingHelpers` output is **not stable** and may gain extra detail or change formatting in any release.

## License

[MIT](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.
