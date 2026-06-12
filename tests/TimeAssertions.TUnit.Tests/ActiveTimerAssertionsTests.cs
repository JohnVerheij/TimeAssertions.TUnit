using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>End-to-end tests for the timer-leak assertions <c>.HasNoActiveTimers()</c> and
/// <c>.HasActiveTimerCount(n)</c>. The TUnit <c>[GenerateAssertion]</c> source generator emits
/// extensions on <c>IAssertionSource&lt;ObservableTimeProvider&gt;</c>; the receiver is an
/// <see cref="ObservableTimeProvider"/> wrapping a <c>FakeTimeProvider</c>, and timers are created
/// and disposed directly to drive the active set. Created timers never fire (fake time is not
/// advanced), so the tests are deterministic.</summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class ActiveTimerAssertionsTests
{
    private static ObservableTimeProvider NewProvider() => new(new FakeTimeProvider());

    /// <summary>A provider with no timers passes the leak check.</summary>
    [Test]
    public async Task NoTimers_HasNoActiveTimers_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(time).HasNoActiveTimers();
    }

    /// <summary>A timer that was created and then disposed leaves the active set empty.</summary>
    [Test]
    public async Task DisposedTimer_HasNoActiveTimers_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        timer.Dispose();
        await Assert.That(time).HasNoActiveTimers();
    }

    /// <summary>An undisposed timer fails the leak check; the message names the survivor by its
    /// schedule and carries the grep-friendly count.</summary>
    [Test]
    public async Task LeakedTimer_HasNoActiveTimers_FailsNamingSurvivor(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimers();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected no active timers but 1 remained");
        await Assert.That(exception.Message).Contains("(count=1)");
        await Assert.That(exception.Message).Contains("[dueTime=1.0s, period=5.0s]");
    }

    /// <summary>A one-shot timer (infinite period) renders as <c>period=one-shot</c> in the
    /// leak message rather than a negative duration.</summary>
    [Test]
    public async Task OneShotLeakedTimer_RendersOneShot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimers();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("period=one-shot");
    }

    /// <summary>The active count matching the expectation passes.</summary>
    [Test]
    public async Task HasActiveTimerCount_Matches_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasActiveTimerCount(2);
    }

    /// <summary>Expecting zero on a fresh provider passes (the registration-half use case at rest).</summary>
    [Test]
    public async Task HasActiveTimerCount_ZeroOnFresh_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(time).HasActiveTimerCount(0);
    }

    /// <summary>A count mismatch fails with both the prose and the grep-friendly trailer.</summary>
    [Test]
    public async Task HasActiveTimerCount_Mismatch_FailsWithMessage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimerCount(2);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected 2 active timer(s) but found 1");
        await Assert.That(exception.Message).Contains("(expected=2, actual=1)");
    }

    /// <summary>Argument validation: a negative expected count is rejected.</summary>
    [Test]
    public async Task HasActiveTimerCount_Negative_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimerCount(-1);
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>The next pending timer's due time within tolerance passes. This is the headline use
    /// case: assert a scheduled backoff delay without advancing the clock.</summary>
    [Test]
    public async Task HasNextTimerDueApproximately_WithinTolerance_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        await Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromSeconds(2), tolerance: TimeSpan.FromMilliseconds(1));
    }

    /// <summary>The smallest enabled due time is the one inspected when several timers are pending.</summary>
    [Test]
    public async Task HasNextTimerDueApproximately_PicksSmallestDueTime(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        await Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromSeconds(1), tolerance: TimeSpan.FromMilliseconds(1));
    }

    /// <summary>An out-of-tolerance due time fails with the expected/actual/delta message.</summary>
    [Test]
    public async Task HasNextTimerDueApproximately_OutOfTolerance_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromMilliseconds(2400), Timeout.InfiniteTimeSpan);

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromSeconds(2), tolerance: TimeSpan.FromMilliseconds(100));
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected the next timer due in approximately 2.0s");
        await Assert.That(exception.Message).Contains("(expected=2000ms, tolerance=100ms, actual=2400ms, delta=400ms)");
    }

    /// <summary>No enabled timer pending fails with the <c>actual=none</c> message rather than
    /// passing vacuously.</summary>
    [Test]
    public async Task HasNextTimerDueApproximately_NoPendingTimer_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromSeconds(2), tolerance: TimeSpan.FromMilliseconds(1));
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("no enabled timer was pending");
        await Assert.That(exception.Message).Contains("actual=none");
    }

    /// <summary>Argument validation: a negative tolerance is rejected.</summary>
    [Test]
    public async Task HasNextTimerDueApproximately_NegativeTolerance_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        await Assert.That(async () =>
        {
            await Assert.That(time).HasNextTimerDueApproximately(TimeSpan.FromSeconds(2), tolerance: TimeSpan.FromMilliseconds(-1));
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>The exponential-backoff motivating scenario: each scheduled step is asserted on the
    /// pending timer without advancing the clock, walking the 500/1000/2000/4000/5000ms ladder.</summary>
    [Test]
    [Arguments(500)]
    [Arguments(1000)]
    [Arguments(2000)]
    [Arguments(4000)]
    [Arguments(5000)]
    public async Task HasNextTimerDueApproximately_BackoffLadder_Passes(int dueMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromMilliseconds(dueMs), Timeout.InfiniteTimeSpan);

        await Assert.That(time).HasNextTimerDueApproximately(
            TimeSpan.FromMilliseconds(dueMs),
            tolerance: TimeSpan.FromMilliseconds(1));
    }

    /// <summary>The next pending timer's due time inside the inclusive range passes.</summary>
    [Test]
    public async Task HasPendingTimerDueWithin_InRange_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        await Assert.That(time).HasPendingTimerDueWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
    }

    /// <summary>The inclusive bounds pass at both ends of the range.</summary>
    [Test]
    [Arguments(1000)]
    [Arguments(4000)]
    public async Task HasPendingTimerDueWithin_InclusiveBounds_Pass(int dueMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromMilliseconds(dueMs), Timeout.InfiniteTimeSpan);

        await Assert.That(time).HasPendingTimerDueWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
    }

    /// <summary>An out-of-range due time fails naming the range and observed due time.</summary>
    [Test]
    public async Task HasPendingTimerDueWithin_OutOfRange_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasPendingTimerDueWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected a pending timer due within [1.0s, 4.0s]");
        await Assert.That(exception.Message).Contains("(min=1000ms, max=4000ms, actual=5000ms)");
    }

    /// <summary>No enabled timer pending fails with the <c>actual=none</c> message.</summary>
    [Test]
    public async Task HasPendingTimerDueWithin_NoPendingTimer_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasPendingTimerDueWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("no enabled timer was pending");
        await Assert.That(exception.Message).Contains("actual=none");
    }

    /// <summary>Argument validation: a max less than min is rejected.</summary>
    [Test]
    public async Task HasPendingTimerDueWithin_MaxLessThanMin_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        await Assert.That(async () =>
        {
            await Assert.That(time).HasPendingTimerDueWithin(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
        }).Throws<ArgumentOutOfRangeException>();
    }

    // ---- Fire-count assertions (v0.9.0) ----

    /// <summary>Advancing fake time past three period boundaries fires a periodic timer three times,
    /// and the cumulative fire count assertion passes.</summary>
    [Test]
    public async Task HasTimerFiredCount_AfterAdvance_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        fake.Advance(TimeSpan.FromSeconds(3));

        await Assert.That(time).HasTimerFiredCount(3);
    }

    /// <summary>A fire-count mismatch names the expected and actual counts with the grep trailer.</summary>
    [Test]
    public async Task HasTimerFiredCount_Mismatch_FailsWithMessage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        fake.Advance(TimeSpan.FromSeconds(1));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerFiredCount(3);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected timer callbacks to have fired 3 time(s) but they fired 1");
        await Assert.That(exception.Message).Contains("(expected=3, actual=1)");
    }

    [Test]
    public async Task HasTimerFiredCount_Negative_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerFiredCount(-1);
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>A scheduled-but-not-advanced timer has not fired, so the no-fire check passes.</summary>
    [Test]
    public async Task HasNoTimerFired_NotAdvanced_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        await Assert.That(time).HasNoTimerFired();
    }

    [Test]
    public async Task HasNoTimerFired_AfterAdvance_FailsWithMessage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        fake.Advance(TimeSpan.FromSeconds(2));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasNoTimerFired();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected no timer callback to have fired but 2 fired");
        await Assert.That(exception.Message).Contains("(expected=0, actual=2)");
    }

    /// <summary>The liveness lower bound passes once the cumulative fire count reaches the minimum.</summary>
    [Test]
    public async Task HasTimerFiredAtLeast_AfterAdvance_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        fake.Advance(TimeSpan.FromSeconds(5));

        await Assert.That(time).HasTimerFiredAtLeast(3);
    }

    [Test]
    public async Task HasTimerFiredAtLeast_Shortfall_FailsWithMessage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        fake.Advance(TimeSpan.FromSeconds(1));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerFiredAtLeast(3);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected timer callbacks to have fired at least 3 time(s) but they fired 1");
        await Assert.That(exception.Message).Contains("(minimum=3, actual=1)");
    }

    [Test]
    public async Task HasTimerFiredAtLeast_Negative_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerFiredAtLeast(-1);
        }).Throws<ArgumentOutOfRangeException>();
    }
}
