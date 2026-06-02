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
}
