using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>
/// End-to-end tests for the real-time "eventually" active-timer assertions
/// (<c>.HasNoActiveTimersEventually(...)</c> / <c>.HasActiveTimerCountEventually(...)</c>) and the
/// synchronous positive-count assertions (<c>.HasActiveTimers()</c> /
/// <c>.HasAtLeastActiveTimerCount(...)</c>). The eventually assertions poll the live
/// <see cref="ObservableTimeProvider.ActiveTimerCount"/> on the real wall clock, so disposal that
/// happens on a background continuation (the hosted-service shutdown shape) is observed without
/// advancing fake time.
/// </summary>
/// <remarks>
/// Robustness: pass cases use a generous timeout (seconds) against an event that fires after a short
/// real delay, so they never depend on tight scheduling; fail cases use a short timeout against a
/// condition that never holds, so total runtime stays small. No test advances fake time to drive
/// disposal (disposal is a real async continuation).
/// </remarks>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class EventuallyActiveTimerAssertionsTests
{
    private static ObservableTimeProvider NewProvider() => new(new FakeTimeProvider());

    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(150);

    /// <summary>An already-clean provider passes without waiting (condition checked before the first
    /// delay).</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_AlreadyClean_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout);
    }

    /// <summary>A timer disposed on a background continuation after a short real delay is observed by
    /// the poll: the assertion passes within the generous timeout.</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_DisposedOnBackgroundContinuation_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        var timer = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        // Dispose on a real async continuation, not inline: this is the hosted-service shutdown
        // shape the assertion exists for. A synchronous leak check just after StopAsync would still
        // see the timer; the poll gives the continuation time to run.
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                timer.Dispose();
            },
            cancellationToken);

        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout);
    }

    /// <summary>A leak that never clears fails once the short timeout elapses; the message names the
    /// surviving timer by its schedule with a grep-friendly count.</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_NeverClears_TimesOutNamingSurvivor(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimersEventually(ShortTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected the active-timer count to reach 0");
        await Assert.That(exception.Message).Contains("(count=1)");
        await Assert.That(exception.Message).Contains("[dueTime=1.0s, period=5.0s]");
    }

    /// <summary>A canceled token cancels the poll loop: an <see cref="OperationCanceledException"/>
    /// propagates rather than an assertion failure.</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_CanceledToken_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, pollingInterval: null, cancellationToken: cts.Token);
        }).Throws<OperationCanceledException>();
    }

    /// <summary>A non-positive poll interval is rejected at construction time.</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_NonPositivePollInterval_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, TimeSpan.Zero, cancellationToken);
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>A negative timeout is rejected at construction time.</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_NegativeTimeout_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimersEventually(TimeSpan.FromMilliseconds(-1));
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>When the assertion source itself throws, the thrown exception is surfaced rather than
    /// masked behind a timeout message.</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_SourceThrows_SurfacesException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(ThrowingProvider).HasNoActiveTimersEventually(GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("InvalidOperationException");
    }

    /// <summary>The count-targeted variant likewise surfaces a throwing source.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_SourceThrows_SurfacesException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(ThrowingProvider).HasActiveTimerCountEventually(0, GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("InvalidOperationException");
    }

    private static ObservableTimeProvider ThrowingProvider() =>
        throw new InvalidOperationException("source evaluation failed");

    private static ObservableTimeProvider NullProvider() => null!;

    /// <summary>The count-targeted variant passes once a background continuation disposes one of two
    /// timers, settling the active set at the expected count.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_SettlesToCount_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        var disposable = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                disposable.Dispose();
            },
            cancellationToken);

        await Assert.That(time).HasActiveTimerCountEventually(1, GenerousTimeout);
    }

    /// <summary>An already-satisfied count passes without waiting.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_AlreadyAtCount_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasActiveTimerCountEventually(1, GenerousTimeout);
    }

    /// <summary>A count that is never reached fails after the short timeout with the
    /// expected/actual trailer.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_NeverReached_TimesOut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimerCountEventually(0, ShortTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected the active-timer count to reach 0");
        await Assert.That(exception.Message).Contains("(expected=0, actual=1)");
    }

    /// <summary>A canceled token cancels the count-targeted poll loop.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_CanceledToken_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimerCountEventually(1, GenerousTimeout, pollingInterval: null, cancellationToken: cts.Token);
        }).Throws<OperationCanceledException>();
    }

    /// <summary>A negative expected count is rejected at construction time.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_NegativeCount_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimerCountEventually(-1, GenerousTimeout);
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>A custom poll interval is honored on the pass path.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_CustomPollInterval_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasActiveTimerCountEventually(1, GenerousTimeout, TimeSpan.FromMilliseconds(10), cancellationToken);
    }

    /// <summary><c>HasActiveTimers()</c> passes when at least one timer is active.</summary>
    [Test]
    public async Task HasActiveTimers_OneActive_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasActiveTimers();
    }

    /// <summary><c>HasActiveTimers()</c> fails on a provider with no active timers, naming the
    /// minimum/actual.</summary>
    [Test]
    public async Task HasActiveTimers_NoneActive_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimers();
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected at least 1 active timer(s) but found 0");
        await Assert.That(exception.Message).Contains("(minimum=1, actual=0)");
    }

    /// <summary><c>HasAtLeastActiveTimerCount(n)</c> passes when the active count meets the lower
    /// bound (and on the exact boundary).</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task HasAtLeastActiveTimerCount_MeetsBound_Passes(int minimum, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasAtLeastActiveTimerCount(minimum);
    }

    /// <summary><c>HasAtLeastActiveTimerCount(0)</c> passes on a fresh provider (a zero lower bound is
    /// always met).</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCount_ZeroBound_PassesOnFresh(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(time).HasAtLeastActiveTimerCount(0);
    }

    /// <summary><c>HasAtLeastActiveTimerCount(n)</c> fails when the active count is below the bound,
    /// naming the minimum/actual and the surviving schedule.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCount_BelowBound_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasAtLeastActiveTimerCount(3);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected at least 3 active timer(s) but found 1");
        await Assert.That(exception.Message).Contains("(minimum=3, actual=1)");
        await Assert.That(exception.Message).Contains("[dueTime=1.0s, period=5.0s]");
    }

    /// <summary>A negative minimum is rejected.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCount_NegativeMinimum_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(async () =>
        {
            await Assert.That(time).HasAtLeastActiveTimerCount(-1);
        }).Throws<ArgumentOutOfRangeException>();
    }

    // --- positional-CancellationToken sugar overloads ---
    // These tests pin overload resolution: the positional (timeout, ct) and (count, timeout, ct)
    // forms must bind to the hand-written sugar, while the bare 1-arg and the named/positional
    // pollingInterval forms keep binding to the canonical generated extension. If the sugar's
    // CancellationToken ever gained a default value the (timeout) call would become a CS0121
    // ambiguity and this file would fail to compile, so the matrix below is the regression guard.

    /// <summary>The positional <c>(timeout, ct)</c> form binds to the sugar overload and passes when
    /// no timer is active (default poll interval is used).</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_PositionalToken_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, cancellationToken);
    }

    /// <summary>The positional <c>(timeout, ct)</c> form honors a canceled token: it cancels the poll
    /// loop and throws <see cref="OperationCanceledException"/>.</summary>
    [Test]
    public async Task HasNoActiveTimersEventually_PositionalToken_Canceled_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, cts.Token);
        }).Throws<OperationCanceledException>();
    }

    /// <summary>The positional <c>(count, timeout, ct)</c> form binds to the sugar overload and passes
    /// when the count is already satisfied.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_PositionalToken_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasActiveTimerCountEventually(1, GenerousTimeout, cancellationToken);
    }

    /// <summary>The positional <c>(count, timeout, ct)</c> form honors a canceled token.</summary>
    [Test]
    public async Task HasActiveTimerCountEventually_PositionalToken_Canceled_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimerCountEventually(1, GenerousTimeout, cts.Token);
        }).Throws<OperationCanceledException>();
    }

    /// <summary>Overload-resolution matrix: every supported call shape compiles and resolves with no
    /// CS0121 ambiguity. The bare <c>(timeout)</c> / <c>(count, timeout)</c> and the
    /// <c>(timeout, pollingInterval, ct)</c> / <c>(count, timeout, pollingInterval, ct)</c> forms bind
    /// to the canonical generated extension; the positional-token forms bind to the sugar.</summary>
    [SuppressMessage(
        "Major Bug",
        "S8949:Pass the cancellationToken to this method",
        Justification = "This test is the overload-resolution matrix: the call shapes that omit the token are precisely what it exists to cover, so passing one would stop exercising them.")]
    [Test]
    public async Task EventuallyOverloadResolution_AllShapesCompileAndResolve(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        var poll = TimeSpan.FromMilliseconds(10);

        // No-timers variants.
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout);                                  // canonical, 1-arg
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, poll);                            // canonical, positional pollingInterval
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, pollingInterval: poll);           // canonical, named pollingInterval
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, poll, cancellationToken);         // canonical, (timeout, pollingInterval, ct)
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, cancellationToken);               // sugar, (timeout, ct)

        // Count variants.
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout);                             // canonical, 2-arg
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout, poll);                       // canonical, positional pollingInterval
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout, pollingInterval: poll);      // canonical, named pollingInterval
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout, poll, cancellationToken);    // canonical, (count, timeout, pollingInterval, ct)
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout, cancellationToken);          // sugar, (count, timeout, ct)
    }

    // --- HasAtLeastActiveTimerCountEventually (async lower bound) ---

    /// <summary>An already-met lower bound passes without waiting.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_AlreadyMet_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasAtLeastActiveTimerCountEventually(1, GenerousTimeout);
    }

    /// <summary>A timer registered on a background continuation lifts the count to the lower bound; the
    /// async lower-bound poll observes it where a synchronous check would race the registration.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_RegistersOnContinuation_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            },
            cancellationToken);

        await Assert.That(time).HasAtLeastActiveTimerCountEventually(1, GenerousTimeout);
    }

    /// <summary>A lower bound that is never reached fails after the short timeout with the
    /// minimum/actual trailer.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_NeverReached_TimesOut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasAtLeastActiveTimerCountEventually(2, ShortTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected the active-timer count to reach at least 2");
        await Assert.That(exception.Message).Contains("(minimum=2, actual=0)");
    }

    /// <summary>A canceled token cancels the lower-bound poll loop.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_CanceledToken_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasAtLeastActiveTimerCountEventually(1, GenerousTimeout, cts.Token);
        }).Throws<OperationCanceledException>();
    }

    /// <summary>A negative minimum is rejected at construction time.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_NegativeCount_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(async () =>
        {
            await Assert.That(time).HasAtLeastActiveTimerCountEventually(-1, GenerousTimeout);
        }).Throws<ArgumentOutOfRangeException>();
    }

    // --- HasAtMostActiveTimerCountEventually (async upper bound) ---

    /// <summary>An already-met upper bound passes without waiting.</summary>
    [Test]
    public async Task HasAtMostActiveTimerCountEventually_AlreadyMet_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(time).HasAtMostActiveTimerCountEventually(1, GenerousTimeout);
    }

    /// <summary>One of two timers disposed on a background continuation brings the count down to the
    /// upper bound; the async upper-bound poll observes it.</summary>
    [Test]
    public async Task HasAtMostActiveTimerCountEventually_SettlesDown_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        var disposable = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                disposable.Dispose();
            },
            cancellationToken);

        await Assert.That(time).HasAtMostActiveTimerCountEventually(1, GenerousTimeout);
    }

    /// <summary>An upper bound that is never reached fails after the short timeout with the
    /// maximum/actual trailer.</summary>
    [Test]
    public async Task HasAtMostActiveTimerCountEventually_NeverReached_TimesOut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasAtMostActiveTimerCountEventually(0, ShortTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected the active-timer count to fall to at most 0");
        await Assert.That(exception.Message).Contains("(maximum=0, actual=2)");
    }

    /// <summary>The positional <c>(count, timeout, ct)</c> form binds to the sugar overload.</summary>
    [Test]
    public async Task HasAtMostActiveTimerCountEventually_PositionalToken_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(time).HasAtMostActiveTimerCountEventually(0, GenerousTimeout, cancellationToken);
    }

    // --- HasActiveTimersEventually (async >= 1) ---

    /// <summary>An already-active provider passes without waiting.</summary>
    [Test]
    public async Task HasActiveTimersEventually_AlreadyActive_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await Assert.That(time).HasActiveTimersEventually(GenerousTimeout);
    }

    /// <summary>A timer registered on a background continuation satisfies the assertion; the poll
    /// observes the registration where a synchronous <c>HasActiveTimers()</c> would race it.</summary>
    [Test]
    public async Task HasActiveTimersEventually_RegistersOnContinuation_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            },
            cancellationToken);

        await Assert.That(time).HasActiveTimersEventually(GenerousTimeout);
    }

    /// <summary>A provider that never registers a timer fails after the short timeout.</summary>
    [Test]
    public async Task HasActiveTimersEventually_NeverActive_TimesOut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimersEventually(ShortTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected at least one active timer");
        await Assert.That(exception.Message).Contains("stayed at 0");
    }

    /// <summary>A canceled token cancels the poll loop.</summary>
    [Test]
    public async Task HasActiveTimersEventually_CanceledToken_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimersEventually(GenerousTimeout, cts.Token);
        }).Throws<OperationCanceledException>();
    }

    // --- source-exception and null-provider guards for the new bounded-count assertions ---

    /// <summary>A throwing source surfaces its exception through the lower-bound assertion.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_SourceThrows_SurfacesException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exception = await Assert.That(async () =>
        {
            await Assert.That(ThrowingProvider).HasAtLeastActiveTimerCountEventually(1, GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("InvalidOperationException");
    }

    /// <summary>A null provider fails the lower-bound assertion with the null-provider message.</summary>
    [Test]
    public async Task HasAtLeastActiveTimerCountEventually_NullProvider_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exception = await Assert.That(async () =>
        {
            await Assert.That(NullProvider).HasAtLeastActiveTimerCountEventually(1, GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the observable time provider was null");
    }

    /// <summary>A throwing source surfaces its exception through the upper-bound assertion.</summary>
    [Test]
    public async Task HasAtMostActiveTimerCountEventually_SourceThrows_SurfacesException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exception = await Assert.That(async () =>
        {
            await Assert.That(ThrowingProvider).HasAtMostActiveTimerCountEventually(1, GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("InvalidOperationException");
    }

    /// <summary>A null provider fails the upper-bound assertion with the null-provider message.</summary>
    [Test]
    public async Task HasAtMostActiveTimerCountEventually_NullProvider_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exception = await Assert.That(async () =>
        {
            await Assert.That(NullProvider).HasAtMostActiveTimerCountEventually(1, GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the observable time provider was null");
    }

    /// <summary>A negative upper bound is rejected at construction time.</summary>
    [Test]
    public async Task HasAtMostActiveTimerCountEventually_NegativeCount_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        await Assert.That(async () =>
        {
            await Assert.That(time).HasAtMostActiveTimerCountEventually(-1, GenerousTimeout);
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>A throwing source surfaces its exception through the at-least-one assertion.</summary>
    [Test]
    public async Task HasActiveTimersEventually_SourceThrows_SurfacesException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exception = await Assert.That(async () =>
        {
            await Assert.That(ThrowingProvider).HasActiveTimersEventually(GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("InvalidOperationException");
    }

    /// <summary>A null provider fails the at-least-one assertion with the null-provider message.</summary>
    [Test]
    public async Task HasActiveTimersEventually_NullProvider_Fails(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exception = await Assert.That(async () =>
        {
            await Assert.That(NullProvider).HasActiveTimersEventually(GenerousTimeout);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("the observable time provider was null");
    }
}
