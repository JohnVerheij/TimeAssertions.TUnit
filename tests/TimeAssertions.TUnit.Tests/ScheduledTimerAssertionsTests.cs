using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>
/// Tests for the due-time assertions: <c>HasTimerScheduled(dueTime, atLeast)</c>, <c>HasNoTimerScheduled()</c>,
/// and the event-driven <c>HasTimerScheduledEventually(...)</c>. They find timers by the due time the code
/// under test passed, among any number of loops, which the earliest-timer assertions
/// (<c>HasNextTimerDueApproximately</c>) cannot.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class ScheduledTimerAssertionsTests
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Hourly = TimeSpan.FromHours(1);
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(10);

    private static ObservableTimeProvider NewProvider() => new(new FakeTimeProvider());

    private static ObservableTimeProvider ThrowingProvider() =>
        throw new InvalidOperationException("source evaluation failed");

    private static ObservableTimeProvider NullProvider() => null!;

    // --- HasTimerScheduled ---

    /// <summary>A timer that is not the earliest one is still found by its due time.</summary>
    [Test]
    public async Task HasTimerScheduled_TimerAmongEarlierOnes_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, Heartbeat, Heartbeat);
        _ = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);

        await Assert.That(time).HasTimerScheduled(PingInterval);
        await Assert.That(time).HasTimerScheduled(Heartbeat);
    }

    /// <summary>The <c>atLeast</c> form tells loops apart: two ping loops are two timers at the ping interval,
    /// whatever else is pending.</summary>
    [Test]
    public async Task HasTimerScheduled_AtLeast_CountsOnlyMatchingTimers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, Hourly, Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, Heartbeat, Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerScheduled(PingInterval, atLeast: 2);
        }).Throws<AssertionException>();
        await Assert.That(exception!.Message).Contains("expected at least 2 active timer(s) scheduled at 30.0s but found 1 (minimum=2, actual=1):");
        await Assert.That(exception.Message).Contains("[dueTime=60:00, period=one-shot]");

        _ = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);
        await Assert.That(time).HasTimerScheduled(PingInterval, atLeast: 2);
    }

    /// <summary>A one-shot timer that fired carries no due time any more, so it is no longer scheduled.</summary>
    [Test]
    public async Task HasTimerScheduled_OneShotFired_NoLongerScheduled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);
        fake.Advance(PingInterval);

        await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerScheduled(PingInterval);
        }).Throws<AssertionException>();
    }

    /// <summary>A negative or infinite due time and a non-positive minimum are rejected.</summary>
    [Test]
    public async Task HasTimerScheduled_InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        await Assert.That(async () => await Assert.That(time).HasTimerScheduled(TimeSpan.FromSeconds(-1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await Assert.That(time).HasTimerScheduled(Timeout.InfiniteTimeSpan))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await Assert.That(time).HasTimerScheduled(PingInterval, atLeast: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    // --- HasNoTimerScheduled ---

    /// <summary>An undisposed but disabled timer is not scheduled: <c>HasNoTimerScheduled</c> passes where
    /// <c>HasNoActiveTimers</c> fails.</summary>
    [Test]
    public async Task HasNoTimerScheduled_DisabledTimer_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fake = new FakeTimeProvider();
        var time = new ObservableTimeProvider(fake);
        _ = time.CreateTimer(static _ => { }, state: null, Heartbeat, Timeout.InfiniteTimeSpan);
        var parked = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);
        parked.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        fake.Advance(Heartbeat);

        await Assert.That(time).HasNoTimerScheduled();
        await Assert.That(time).HasActiveTimerCount(2);
    }

    /// <summary>A pending timer fails the assertion; the message lists only the scheduled timers.</summary>
    [Test]
    public async Task HasNoTimerScheduled_PendingTimer_FailsNamingIt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasNoTimerScheduled();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected no scheduled timer but 1 active timer(s) had a pending due time (count=1):");
        await Assert.That(exception.Message).Contains("[dueTime=30.0s, period=one-shot]");
        await Assert.That(exception.Message).DoesNotContain("dueTime=infinite");
    }

    // --- HasTimerScheduledEventually ---

    /// <summary>Two ping loops started on background continuations: the token-only form waits until both armed
    /// their delay, even though other timers were pending first.</summary>
    [Test]
    public async Task HasTimerScheduledEventually_TwoLoopsArmOnBackground_Passes(CancellationToken cancellationToken)
    {
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, Hourly, Timeout.InfiniteTimeSpan);
        _ = time.CreateTimer(static _ => { }, state: null, Heartbeat, Timeout.InfiniteTimeSpan);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                _ = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);
                await Task.Yield();
                _ = time.CreateTimer(static _ => { }, state: null, PingInterval, Timeout.InfiniteTimeSpan);
            },
            cancellationToken);

        await Assert.That(time).HasTimerScheduledEventually(PingInterval, atLeast: 2, cancellationToken);
        await Assert.That(time).HasTimerScheduled(PingInterval, atLeast: 2);
    }

    /// <summary>The single-timer forms (with and without a timeout) pass once one timer carries the due time,
    /// including one rescheduled to it.</summary>
    [Test]
    public async Task HasTimerScheduledEventually_SingleTimerForms_Pass(CancellationToken cancellationToken)
    {
        var time = NewProvider();
        var timer = time.CreateTimer(static _ => { }, state: null, Heartbeat, Timeout.InfiniteTimeSpan);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                timer.Change(PingInterval, Timeout.InfiniteTimeSpan);
            },
            cancellationToken);

        await Assert.That(time).HasTimerScheduledEventually(PingInterval, cancellationToken);
        await Assert.That(time).HasTimerScheduledEventually(PingInterval, GenerousTimeout, cancellationToken);
        await Assert.That(time).HasTimerScheduledEventually(PingInterval, atLeast: 1, GenerousTimeout, cancellationToken);
    }

    /// <summary>A due time that never appears fails at the bound; the message names the shortfall, the bound,
    /// and the timers that are scheduled.</summary>
    [Test]
    public async Task HasTimerScheduledEventually_NeverScheduled_FailsAtTheBound(CancellationToken cancellationToken)
    {
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, Heartbeat, Heartbeat);

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerScheduledEventually(PingInterval, atLeast: 2, TimeSpan.Zero, cancellationToken);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected at least 2 active timer(s) scheduled at 30.0s within");
        await Assert.That(exception.Message).Contains("(minimum=2, actual=0)");
        await Assert.That(exception.Message).Contains("[dueTime=1.0s, period=1.0s]");
        await Assert.That(exception.Message).Contains(".HasTimerScheduledEventually(30.0s, atLeast: 2,");
    }

    /// <summary>Canceling the token while the token-only form waits throws
    /// <see cref="OperationCanceledException"/>.</summary>
    [Test]
    public async Task HasTimerScheduledEventually_CanceledWhileWaiting_Throws(CancellationToken cancellationToken)
    {
        var time = NewProvider();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                await cts.CancelAsync();
            },
            cancellationToken);

        await Assert.That(async () =>
        {
            await Assert.That(time).HasTimerScheduledEventually(PingInterval, cts.Token);
        }).Throws<OperationCanceledException>();
    }

    /// <summary>Invalid arguments are rejected when the assertion is built.</summary>
    [Test]
    public async Task HasTimerScheduledEventually_InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        var time = NewProvider();

        await Assert.That(async () => await Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(-1), cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await Assert.That(time).HasTimerScheduledEventually(PingInterval, atLeast: 0, cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await Assert.That(time).HasTimerScheduledEventually(PingInterval, TimeSpan.FromSeconds(-1), cancellationToken))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>A throwing source is surfaced rather than masked, and a null provider fails clearly.</summary>
    [Test]
    public async Task HasTimerScheduledEventually_SourceThrowsOrNull_Fails(CancellationToken cancellationToken)
    {
        var thrown = await Assert.That(async () =>
        {
            await Assert.That(ThrowingProvider).HasTimerScheduledEventually(PingInterval, GenerousTimeout, cancellationToken);
        }).Throws<AssertionException>();
        await Assert.That(thrown!.Message).Contains("InvalidOperationException");

        var nullSource = await Assert.That(async () =>
        {
            await Assert.That(NullProvider).HasTimerScheduledEventually(PingInterval, GenerousTimeout, cancellationToken);
        }).Throws<AssertionException>();
        await Assert.That(nullSource!.Message).Contains("the observable time provider was null");
    }

    // --- one object: the observable fake ---

    /// <summary>Every timer assertion applies to an <see cref="ObservableFakeTimeProvider"/> directly: inject it,
    /// advance it, and assert on it, with no second object.</summary>
    [Test]
    public async Task ObservableFakeTimeProvider_AssertionsApplyDirectly(CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = PingLoopAsync(time, stop.Token);

        await Assert.That(time).HasTimerScheduledEventually(PingInterval, cancellationToken);
        await time.AdvanceAndSettleAsync(PingInterval, cancellationToken);
        await Assert.That(time).HasTimerFiredCount(1);
        await Assert.That(time).HasTimerScheduled(PingInterval);

        await stop.CancelAsync();
        await loop;
        await Assert.That(time).HasNoActiveTimersEventually(cancellationToken);
        await Assert.That(time).HasNoTimerScheduled();
    }

    /// <summary>A ping loop: one delay of the ping interval per iteration until <paramref name="stop"/> is
    /// canceled.</summary>
    private static async Task PingLoopAsync(TimeProvider time, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(PingInterval, time, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped by the test.
        }
    }
}
