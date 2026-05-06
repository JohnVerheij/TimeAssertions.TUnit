# TimeAssertions.TUnit

> **Scope:** Test projects only. Not intended for production code.

**The TUnit assertion package for projects committed to `TimeProvider`-based testable time.**

Fluent assertions on `FakeTimeProvider` state, `TimeProvider`-aware `DateTimeOffset` recency / past / future checks, plus the cross-cutting `.WithinTimeBudget(TimeSpan)` chain extension for assertion-level timing budgets.

```csharp
using Microsoft.Extensions.Time.Testing;

var fakeTime = new FakeTimeProvider();
fakeTime.Advance(TimeSpan.FromMinutes(31));

await Assert.That(fakeTime).HasAdvanced(TimeSpan.FromMinutes(31));
await Assert.That(timestamp).IsRecent(TimeSpan.FromSeconds(1), fakeTime);
await Assert.That(timestamp).IsBeforeNow(fakeTime);

// Cross-cutting timing budget on any chain
await Assert.That(asyncOp)
    .IsEqualTo(42)
    .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
```

## What this package does

- **`FakeTimeProvider` assertions** — `HasAdvanced`, `HasAdvancedBy`, `HasUtcNow` for verifying the fake clock's state after `Advance` / `SetUtcNow` calls
- **`TimeProvider`-aware `DateTimeOffset` assertions** — `IsRecent(TimeSpan, TimeProvider?)`, `IsBeforeNow(TimeProvider)`, `IsAfterNow(TimeProvider)` for time-relative checks against a (possibly fake) clock
- **`.And.WithinTimeBudget(TimeSpan)`** — assertion-level timing budget that composes with any behavioural assertion via `.And`

`Microsoft.Extensions.TimeProvider.Testing` is propagated transitively so `FakeTimeProvider` is available in consuming test projects without an extra explicit reference.

## Quick start

```bash
dotnet add package TimeAssertions.TUnit
```

The assertions auto-import via `TUnit.Assertions.Extensions`; no extra `using` directive is needed if your project already uses TUnit. Add `using Microsoft.Extensions.Time.Testing;` to construct `FakeTimeProvider` instances.

## Family

- [LogAssertions.TUnit](https://github.com/JohnVerheij/LogAssertions.TUnit)
- [SnapshotAssertions.TUnit](https://github.com/JohnVerheij/SnapshotAssertions.TUnit)

## Documentation

[github.com/JohnVerheij/TimeAssertions.TUnit](https://github.com/JohnVerheij/TimeAssertions.TUnit) — full README, design notes, "Why TimeProvider in tests" section, examples by use case.

## License

[MIT](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/LICENSE)
