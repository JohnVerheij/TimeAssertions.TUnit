# TimeAssertions

> **Scope:** Test projects only. Not intended for production code.

Framework-agnostic core for the [TimeAssertions.TUnit](https://www.nuget.org/packages/TimeAssertions.TUnit/) package family. Most users should install **`TimeAssertions.TUnit`** instead — it depends on this package transitively and ships the actual TUnit assertion entry points.

## What this package contains

- **`TimeRenderingHelpers`** — formatting utilities for elapsed durations and budgets in failure-message context. Pure, allocation-conscious.

## When to install this package directly

Only when authoring a non-TUnit adapter for the assertion family (e.g. an xUnit / NUnit / MSTest adapter). For any other use case, install `TimeAssertions.TUnit`.

## Repository

[github.com/JohnVerheij/TimeAssertions.TUnit](https://github.com/JohnVerheij/TimeAssertions.TUnit) — full README, design notes, examples.

## License

[MIT](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/LICENSE)
