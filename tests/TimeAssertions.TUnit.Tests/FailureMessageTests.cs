using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>
/// Pins the full failure message of every assertion: TUnit's <c>Expected …</c> line (the expectation), the
/// <c>but …</c> text (only what was found, with its trailer and timer list), and the <c>at Assert.That(…)</c> line,
/// which names the call once. The README samples are copied from these texts. Line endings are normalized, so the
/// pins hold on every platform.
/// </summary>
[Timeout(10_000)]
internal sealed class FailureMessageTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The bound of the "eventually" forms: real time, so short.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromMilliseconds(50);

    /// <summary>Joins message lines with <c>\n</c>, the line ending the captured messages are normalized to.</summary>
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>Runs <paramref name="assertion"/>, which must fail, and returns its message with normalized line endings.</summary>
    private static async Task<string> FailureOf(Func<Task> assertion)
    {
        var exception = await Assert.That(assertion).Throws<AssertionException>();
        return exception!.Message.ReplaceLineEndings("\n");
    }

    /// <summary>A provider with a 600 ms one-shot timer and a 30 s timer that repeats every second.</summary>
    private static (FakeTimeProvider Fake, ObservableTimeProvider Time) TwoTimers()
    {
        var fake = new FakeTimeProvider(Start);
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
        return (fake, time);
    }

    private const string TwoTimerSchedules = "  [dueTime=600ms, period=one-shot]\n  [dueTime=30.0s, period=1.0s]";

    // --- active-timer and due-time assertions ---

    [Test]
    public async Task HasNoActiveTimers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () => await Assert.That(time).HasNoActiveTimers());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have no active timers",
            "but 2 remained (count=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasNoActiveTimers()"));
    }

    [Test]
    public async Task HasActiveTimerCount(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () => await Assert.That(time).HasActiveTimerCount(3));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have 3 active timer(s)",
            "but found 2 (expected=3, actual=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasActiveTimerCount(3)"));
    }

    [Test]
    public async Task HasActiveTimers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var empty = new ObservableTimeProvider(new FakeTimeProvider(Start));

        var message = await FailureOf(async () => await Assert.That(empty).HasActiveTimers());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have at least one active timer",
            "but found 0 (minimum=1, actual=0)",
            string.Empty,
            "at Assert.That(empty).HasActiveTimers()"));
    }

    [Test]
    public async Task HasAtLeastActiveTimerCount(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () => await Assert.That(time).HasAtLeastActiveTimerCount(3));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have at least 3 active timer(s)",
            "but found 2 (minimum=3, actual=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasAtLeastActiveTimerCount(3)"));
    }

    [Test]
    public async Task HasNextTimerDueApproximately(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () =>
            await Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have the next timer due in approximately 00:00:00.5000000 within tolerance 00:00:00.0010000",
            "but it was due in 600ms, off by 100ms (expected=500ms, tolerance=1ms, actual=600ms, delta=100ms)",
            string.Empty,
            "at Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1))"));
    }

    [Test]
    public async Task HasPendingTimerDueWithin(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () =>
            await Assert.That(time).HasPendingTimerDueWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have the next timer due within [00:00:01, 00:00:02]",
            "but it was due in 600ms (min=1000ms, max=2000ms, actual=600ms)",
            string.Empty,
            "at Assert.That(time).HasPendingTimerDueWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))"));
    }

    [Test]
    public async Task HasTimerScheduled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () => await Assert.That(time).HasTimerScheduled(TimeSpan.FromSeconds(30), 2));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have at least 2 active timer(s) scheduled at 00:00:30",
            "but found 1 (minimum=2, actual=1) among 2 active timer(s):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasTimerScheduled(TimeSpan.FromSeconds(30), 2)"));
    }

    [Test]
    public async Task HasNoTimerScheduled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () => await Assert.That(time).HasNoTimerScheduled());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have no scheduled timer",
            "but 2 active timer(s) had a pending due time (count=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasNoTimerScheduled()"));
    }

    // --- fire counts ---

    private const string FiredSchedules =
        "  [dueTime=infinite, period=one-shot, fired=1]\n  [dueTime=30.0s, period=1.0s, fired=0]";

    [Test]
    public async Task HasTimerFiredCount(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (fake, time) = TwoTimers();
        fake.Advance(TimeSpan.FromSeconds(1));

        var message = await FailureOf(async () => await Assert.That(time).HasTimerFiredCount(3));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have fired timer callbacks 3 time(s)",
            "but they fired 1 time(s) (expected=3, actual=1):",
            FiredSchedules,
            string.Empty,
            "at Assert.That(time).HasTimerFiredCount(3)"));
    }

    [Test]
    public async Task HasNoTimerFired(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (fake, time) = TwoTimers();
        fake.Advance(TimeSpan.FromSeconds(1));

        var message = await FailureOf(async () => await Assert.That(time).HasNoTimerFired());

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have fired no timer callback",
            "but 1 fired (expected=0, actual=1):",
            FiredSchedules,
            string.Empty,
            "at Assert.That(time).HasNoTimerFired()"));
    }

    [Test]
    public async Task HasTimerFiredAtLeast(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (fake, time) = TwoTimers();
        fake.Advance(TimeSpan.FromSeconds(1));

        var message = await FailureOf(async () => await Assert.That(time).HasTimerFiredAtLeast(5));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have fired timer callbacks at least 5 time(s)",
            "but they fired 1 time(s) (minimum=5, actual=1):",
            FiredSchedules,
            string.Empty,
            "at Assert.That(time).HasTimerFiredAtLeast(5)"));
    }

    // --- rate limit ---

    [Test]
    public async Task WasInvokedAtMostOncePer(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DateTimeOffset> stamps = [Start, Start.AddSeconds(30), Start.AddSeconds(35)];

        var message = await FailureOf(async () => await Assert.That(stamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have consecutive timestamps at least 00:00:30 apart",
            "but the gap at index 2 was 5.0s (minimum 30.0s)",
            "  timestamps[1]: 2026-01-01T00:00:30.0000000+00:00",
            "  timestamps[2]: 2026-01-01T00:00:35.0000000+00:00",
            "  (gap=5000ms, minimum=30000ms)",
            string.Empty,
            "at Assert.That(stamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30))"));
    }

    // --- the fake clock's own state ---

    private static FakeTimeProvider AdvancedThirtyMinutes()
    {
        var fake = new FakeTimeProvider(Start);
        fake.Advance(TimeSpan.FromMinutes(30));
        return fake;
    }

    [Test]
    public async Task HasAdvancedExactly(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fakeTime = AdvancedThirtyMinutes();

        var message = await FailureOf(async () => await Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(31)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have advanced by total 00:31:00",
            "but it advanced by 00:30:00, off by 00:01:00 (start 2026-01-01T00:00:00.0000000+00:00, now 2026-01-01T00:30:00.0000000+00:00)",
            string.Empty,
            "at Assert.That(fakeTime).HasAdvancedExactly(TimeSpan.FromMinutes(31))"));
    }

    [Test]
    public async Task HasAdvancedApproximately(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fakeTime = AdvancedThirtyMinutes();

        var message = await FailureOf(async () =>
            await Assert.That(fakeTime).HasAdvancedApproximately(TimeSpan.FromMinutes(31), TimeSpan.FromSeconds(1)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have advanced by approximately 00:31:00 within tolerance 00:00:01",
            "but it advanced by 00:30:00, off by 00:01:00 (start 2026-01-01T00:00:00.0000000+00:00, now 2026-01-01T00:30:00.0000000+00:00)",
            string.Empty,
            "at Assert.That(fakeTime).HasAdvancedApproximately(TimeSpan.FromMinutes(31), TimeSpan.FromSeconds(1))"));
    }

    [Test]
    public async Task HasUtcNow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fakeTime = AdvancedThirtyMinutes();

        var message = await FailureOf(async () => await Assert.That(fakeTime).HasUtcNow(Start));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have UTC now equal to the expected time",
            "but it was 2026-01-01T00:30:00.0000000+00:00, expected 2026-01-01T00:00:00.0000000+00:00, off by 00:30:00",
            string.Empty,
            "at Assert.That(fakeTime).HasUtcNow(Start)"));
    }

    [Test]
    public async Task HasUtcNowApproximately(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fakeTime = AdvancedThirtyMinutes();

        var message = await FailureOf(async () =>
            await Assert.That(fakeTime).HasUtcNowApproximately(Start, TimeSpan.FromSeconds(1)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to have UTC now within tolerance 00:00:01 of the expected time",
            "but it was 2026-01-01T00:30:00.0000000+00:00, expected 2026-01-01T00:00:00.0000000+00:00, off by 00:30:00",
            string.Empty,
            "at Assert.That(fakeTime).HasUtcNowApproximately(Start, TimeSpan.FromSeconds(1))"));
    }

    /// <summary>The fake-clock messages read the same under any culture.</summary>
    [Test]
    [NotInParallel(nameof(CultureInfo))]
    public async Task HasUtcNow_UnderAnotherCulture_IsCultureInvariant(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fakeTime = AdvancedThirtyMinutes();
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            var message = await FailureOf(async () => await Assert.That(fakeTime).HasUtcNow(Start));

            await Assert.That(message).Contains(
                "but it was 2026-01-01T00:30:00.0000000+00:00, expected 2026-01-01T00:00:00.0000000+00:00, off by 00:30:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // --- the "eventually" forms ---

    [Test]
    public async Task HasNoActiveTimersEventually(CancellationToken cancellationToken)
    {
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () => await Assert.That(time).HasNoActiveTimersEventually(Bound, cancellationToken));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected no active timers within 50ms",
            "but 2 remained (count=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasNoActiveTimersEventually(Bound, cancellationToken)"));
    }

    [Test]
    public async Task HasActiveTimerCountEventually(CancellationToken cancellationToken)
    {
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () => await Assert.That(time).HasActiveTimerCountEventually(3, Bound, cancellationToken));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected 3 active timer(s) within 50ms",
            "but found 2 (expected=3, actual=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasActiveTimerCountEventually(3, Bound, cancellationToken)"));
    }

    [Test]
    public async Task HasAtLeastActiveTimerCountEventually(CancellationToken cancellationToken)
    {
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () =>
            await Assert.That(time).HasAtLeastActiveTimerCountEventually(3, Bound, cancellationToken));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected at least 3 active timer(s) within 50ms",
            "but found 2 (minimum=3, actual=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasAtLeastActiveTimerCountEventually(3, Bound, cancellationToken)"));
    }

    [Test]
    public async Task HasAtMostActiveTimerCountEventually(CancellationToken cancellationToken)
    {
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () =>
            await Assert.That(time).HasAtMostActiveTimerCountEventually(1, Bound, cancellationToken));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected at most 1 active timer(s) within 50ms",
            "but found 2 (maximum=1, actual=2):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasAtMostActiveTimerCountEventually(1, Bound, cancellationToken)"));
    }

    [Test]
    public async Task HasActiveTimersEventually(CancellationToken cancellationToken)
    {
        var empty = new ObservableTimeProvider(new FakeTimeProvider(Start));

        var message = await FailureOf(async () => await Assert.That(empty).HasActiveTimersEventually(Bound, cancellationToken));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected at least one active timer within 50ms",
            "but the active-timer count stayed at 0",
            string.Empty,
            "at Assert.That(empty).HasActiveTimersEventually(Bound, cancellationToken)"));
    }

    [Test]
    public async Task HasTimerScheduledEventually(CancellationToken cancellationToken)
    {
        var (_, time) = TwoTimers();

        var message = await FailureOf(async () =>
            await Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(30), 2, Bound, cancellationToken));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected at least 2 active timer(s) scheduled at 30.0s within 50ms",
            "but found 1 (minimum=2, actual=1) among 2 active timer(s):",
            TwoTimerSchedules,
            string.Empty,
            "at Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(30), 2, Bound, cancellationToken)"));
    }

    // --- the token ends an "eventually" wait: the cancellation says why ---

    /// <summary>Runs <paramref name="assertion"/> with a canceled token and returns the cancellation's message.</summary>
    private static async Task<string> CancellationOf(Func<CancellationToken, Task> assertion)
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var exception = await Assert.That(() => assertion(canceled.Token)).ThrowsExactly<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(canceled.Token);
        return exception.Message.ReplaceLineEndings("\n");
    }

    [Test]
    public async Task HasNoActiveTimersEventually_TokenCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await CancellationOf(async token => await Assert.That(time).HasNoActiveTimersEventually(token));

        await Assert.That(message).IsEqualTo(Lines(
            "Canceled while waiting for no active timers: 2 remained (count=2):",
            TwoTimerSchedules));
    }

    [Test]
    public async Task HasNoActiveTimersEventually_TimeoutFormTokenCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await CancellationOf(async token =>
            await Assert.That(time).HasNoActiveTimersEventually(TimeSpan.FromSeconds(5), token));

        await Assert.That(message).IsEqualTo(Lines(
            "Canceled while waiting for no active timers within 5.0s: 2 remained (count=2):",
            TwoTimerSchedules));
    }

    [Test]
    public async Task HasActiveTimerCountEventually_TokenCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await CancellationOf(async token => await Assert.That(time).HasActiveTimerCountEventually(3, token));

        await Assert.That(message).IsEqualTo(Lines(
            "Canceled while waiting for 3 active timer(s): found 2 (expected=3, actual=2):",
            TwoTimerSchedules));
    }

    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_TokenCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await CancellationOf(async token =>
            await Assert.That(time).HasAtLeastActiveTimerCountEventually(3, token));

        await Assert.That(message).IsEqualTo(Lines(
            "Canceled while waiting for at least 3 active timer(s): found 2 (minimum=3, actual=2):",
            TwoTimerSchedules));
    }

    [Test]
    public async Task HasAtMostActiveTimerCountEventually_TokenCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await CancellationOf(async token =>
            await Assert.That(time).HasAtMostActiveTimerCountEventually(1, token));

        await Assert.That(message).IsEqualTo(Lines(
            "Canceled while waiting for at most 1 active timer(s): found 2 (maximum=1, actual=2):",
            TwoTimerSchedules));
    }

    [Test]
    public async Task HasActiveTimersEventually_TokenCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var empty = new ObservableTimeProvider(new FakeTimeProvider(Start));

        var message = await CancellationOf(async token => await Assert.That(empty).HasActiveTimersEventually(token));

        await Assert.That(message).IsEqualTo(
            "Canceled while waiting for at least one active timer: the active-timer count stayed at 0");
    }

    [Test]
    public async Task HasTimerScheduledEventually_TokenCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, time) = TwoTimers();

        var message = await CancellationOf(async token =>
            await Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(30), 2, token));

        await Assert.That(message).IsEqualTo(Lines(
            "Canceled while waiting for at least 2 active timer(s) scheduled at 30.0s: found 1 (minimum=2, actual=1) among 2 active timer(s):",
            TwoTimerSchedules));
    }

    // --- timing budget: the measured durations are real time, so only they are left open ---

    private static async Task<int> SlowAsync()
    {
        await Task.Delay(20);
        return 42;
    }

    private static async Task<int> RefusedAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("connection refused");
    }

    private static async Task MatchesAsync(string message, string pattern) =>
        await Assert.That(Regex.IsMatch(message, pattern, RegexOptions.None, TimeSpan.FromSeconds(1))).IsTrue();

    [Test]
    public async Task WithinTimeBudget_Overrun(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<Task<int>> slow = SlowAsync;

        var message = await FailureOf(async () =>
            await Assert.That(slow).IsEqualTo(42).And.WithinTimeBudget(TimeSpan.FromMilliseconds(1)));

        await MatchesAsync(message, string.Join(
            "\n",
            @"^Expected to be 42",
            @"\tand completion within timing budget of 1ms",
            @"but completed in \S+: exceeded budget of 1ms by \S+ \(elapsed=\d+ms, budget=1ms, overrun=\d+ms\)",
            string.Empty,
            @"at Assert\.That\(slow\)\.IsEqualTo\(42\)\.And\.WithinTimeBudget\(TimeSpan\.FromMilliseconds\(1\)\)$"));
    }

    [Test]
    public async Task WithinTimeBudgetCapturing_Overrun(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<Task<int>> slow = SlowAsync;
        var elapsed = TimeSpan.Zero;

        var message = await FailureOf(async () =>
            await Assert.That(slow).IsEqualTo(42).And.WithinTimeBudgetCapturing(TimeSpan.FromMilliseconds(1), e => elapsed = e));

        await MatchesAsync(message, string.Join(
            "\n",
            @"^Expected to be 42",
            @"\tand completion within timing budget of 1ms \(capturing elapsed\)",
            @"but completed in \S+: exceeded budget of 1ms by \S+ \(elapsed=\d+ms, budget=1ms, overrun=\d+ms\)",
            string.Empty,
            @"at Assert\.That\(slow\)\.IsEqualTo\(42\)\.And\.WithinTimeBudgetCapturing\(TimeSpan\.FromMilliseconds\(1\), e => elapsed = e\)$"));
        await Assert.That(elapsed).IsGreaterThan(TimeSpan.FromMilliseconds(1));
    }

    /// <summary>A source that throws, with the budget first in the chain: the budget assertion reports it.</summary>
    [Test]
    public async Task WithinTimeBudget_SourceThrew(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<Task<int>> refused = RefusedAsync;

        var message = await FailureOf(async () => await Assert.That(refused).WithinTimeBudget(TimeSpan.FromMilliseconds(500)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected completion within timing budget of 500ms",
            "but threw InvalidOperationException: connection refused",
            string.Empty,
            "at Assert.That(refused).WithinTimeBudget(TimeSpan.FromMilliseconds(500))"));
    }

    /// <summary>A source that throws, after a behavioral assertion: that assertion reports it first.</summary>
    [Test]
    public async Task WithinTimeBudget_SourceThrewAfterIsEqualTo(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<Task<int>> refused = RefusedAsync;

        var message = await FailureOf(async () =>
            await Assert.That(refused).IsEqualTo(42).And.WithinTimeBudget(TimeSpan.FromMilliseconds(500)));

        await Assert.That(message).IsEqualTo(Lines(
            "Expected to be 42",
            "but threw InvalidOperationException",
            string.Empty,
            "at Assert.That(refused).IsEqualTo(42).And.WithinTimeBudget(TimeSpan.FromMilliseconds(500))"));
    }

    // --- IsRecent validates its window ---

    [Test]
    public async Task IsRecent_NegativeWindow_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fakeTime = new FakeTimeProvider(Start);
        var timestamp = Start;

        var exception = await Assert.That(async () =>
                await Assert.That(timestamp).IsRecent(TimeSpan.FromSeconds(-1), fakeTime))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(exception!.ParamName).IsEqualTo("window");
    }
}
