using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>
/// Tests for the deprecated <c>pollingInterval</c> forms of the "eventually" active-timer assertions. Since
/// 0.10.0 the waits are event-driven, so these forms ignore the interval and forward to the form without it.
/// The tests pin that every pre-0.10.0 call shape still compiles, binds, and behaves like its replacement until
/// 0.11.0 removes the overloads.
/// </summary>
[SuppressMessage(
    "Info Code Smell",
    "S1133:Deprecated code should be removed",
    Justification = "These tests cover the deprecated overloads for as long as they ship; both are removed together in 0.11.0.")]
[Obsolete("Covers the deprecated pollingInterval overloads, which are removed in 0.11.0.")]
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class EventuallyPollingIntervalObsoleteTests
{
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(2);

    private static ObservableTimeProvider NewProvider() => new(new FakeTimeProvider());

    /// <summary>Every pre-0.10.0 call shape with a <c>pollingInterval</c> argument (positional, named, and
    /// followed by a token) still binds to the deprecated overloads and passes.</summary>
    [Test]
    public async Task PollingIntervalShapes_StillBindAndPass(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        TimeSpan? poll = TimeSpan.FromMilliseconds(10);

        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, poll, cancellationToken);
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, pollingInterval: poll, cancellationToken: cancellationToken);
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout, poll, cancellationToken);
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout, pollingInterval: poll, cancellationToken: cancellationToken);
        await Assert.That(time).HasAtLeastActiveTimerCountEventually(0, GenerousTimeout, poll, cancellationToken);
        await Assert.That(time).HasAtMostActiveTimerCountEventually(0, GenerousTimeout, poll, cancellationToken);

        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        await Assert.That(time).HasActiveTimersEventually(GenerousTimeout, poll, cancellationToken);
    }

    /// <summary>A null or non-positive interval no longer throws: the interval is ignored, where 0.9.0
    /// rejected a non-positive one.</summary>
    [Test]
    public async Task NonPositivePollingInterval_IsIgnored(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();

        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, TimeSpan.Zero, cancellationToken);
        await Assert.That(time).HasNoActiveTimersEventually(GenerousTimeout, TimeSpan.FromMilliseconds(-1), cancellationToken);
        await Assert.That(time).HasActiveTimerCountEventually(0, GenerousTimeout, null, cancellationToken);
    }

    /// <summary>The deprecated forms fail like their replacements: same message, same count rendering.</summary>
    [Test]
    public async Task PollingIntervalShape_NeverReached_FailsWithReplacementMessage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        _ = time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        var exception = await Assert.That(async () =>
        {
            await Assert.That(time).HasActiveTimerCountEventually(0, TimeSpan.Zero, TimeSpan.FromMilliseconds(10), cancellationToken);
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("expected the active-timer count to reach 0 within");
        await Assert.That(exception.Message).Contains("(expected=0, actual=1)");
    }

    /// <summary>The deprecated forms still validate the timeout and the count.</summary>
    [Test]
    public async Task PollingIntervalShape_InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = NewProvider();
        var poll = TimeSpan.FromMilliseconds(10);

        await Assert.That(async () =>
        {
            await Assert.That(time).HasNoActiveTimersEventually(TimeSpan.FromMilliseconds(-1), poll, cancellationToken);
        }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () =>
        {
            await Assert.That(time).HasAtMostActiveTimerCountEventually(-1, GenerousTimeout, poll, cancellationToken);
        }).Throws<ArgumentOutOfRangeException>();
    }
}
