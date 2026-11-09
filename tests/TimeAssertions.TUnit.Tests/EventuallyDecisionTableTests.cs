using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using TimeAssertions;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>
/// The six "eventually" timer assertions as a decision table: the condition is met before the wait, met during it
/// (by a change on another thread), never met (the <c>timeout</c> ends the wait and the assertion fails), or the token
/// ends the wait (an <see cref="OperationCanceledException"/> that says what was awaited).
/// </summary>
[Timeout(15_000)]
internal sealed class EventuallyDecisionTableTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    /// <summary>The assertion under test.</summary>
    internal enum Eventually
    {
        /// <summary><c>HasNoActiveTimersEventually</c>.</summary>
        NoActiveTimers,

        /// <summary><c>HasActiveTimersEventually</c>.</summary>
        ActiveTimers,

        /// <summary><c>HasActiveTimerCountEventually(2)</c>.</summary>
        ActiveTimerCountOfTwo,

        /// <summary><c>HasAtLeastActiveTimerCountEventually(2)</c>.</summary>
        AtLeastTwoActiveTimers,

        /// <summary><c>HasAtMostActiveTimerCountEventually(1)</c>.</summary>
        AtMostOneActiveTimer,

        /// <summary><c>HasTimerScheduledEventually(30 s)</c>.</summary>
        TimerScheduledAtThirtySeconds,
    }

    /// <summary>When the condition holds.</summary>
    internal enum Outcome
    {
        /// <summary>Before the wait starts.</summary>
        MetBefore,

        /// <summary>During the wait, through a change on another thread.</summary>
        MetDuring,

        /// <summary>Never: the timeout ends the wait.</summary>
        Never,

        /// <summary>Never, and the token ends the wait.</summary>
        Canceled,
    }

    /// <summary>One row: the assertion and the outcome.</summary>
    [Test]
    [MethodDataSource(nameof(Rows))]
    public async Task EventuallyAssertion_Outcome(Eventually assertion, Outcome outcome, CancellationToken cancellationToken)
    {
        var scenario = new Scenario(assertion);
        if (outcome is Outcome.MetBefore)
        {
            scenario.MakeMet();
        }

        switch (outcome)
        {
            case Outcome.MetBefore:
                await scenario.AssertAsync(Generous, cancellationToken);
                break;
            case Outcome.MetDuring:
                var waiting = scenario.AssertAsync(Generous, cancellationToken);
                await Assert.That(waiting.IsCompleted).IsFalse().Because("the assertion must not have finished before the change");
                await Task.Run(scenario.MakeMet, cancellationToken);
                await waiting;
                break;
            case Outcome.Never:
                await Assert.That(async () => await scenario.AssertAsync(Short, cancellationToken)).Throws<AssertionException>();
                break;
            case Outcome.Canceled:
                using (var canceled = new CancellationTokenSource())
                {
                    await canceled.CancelAsync();
                    var exception = await Assert.That(async () => await scenario.AssertAsync(timeout: null, canceled.Token))
                        .ThrowsExactly<OperationCanceledException>();
                    await Assert.That(exception!.Message).StartsWith("Canceled while waiting for ");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, message: null);
        }
    }

    /// <summary>Every assertion with every outcome.</summary>
    /// <returns>The rows.</returns>
    public static IEnumerable<(Eventually Assertion, Outcome Outcome)> Rows()
    {
        foreach (var assertion in Enum.GetValues<Eventually>())
        {
            foreach (var outcome in Enum.GetValues<Outcome>())
            {
                yield return (assertion, outcome);
            }
        }
    }

    /// <summary>A provider whose state does not meet the row's condition until <see cref="MakeMet"/>.</summary>
    private sealed class Scenario
    {
        private readonly Eventually _assertion;
        private readonly ObservableTimeProvider _time = new(new FakeTimeProvider());
        private readonly List<ITimer> _timers = [];

        public Scenario(Eventually assertion)
        {
            _assertion = assertion;
            var unmet = assertion switch
            {
                Eventually.NoActiveTimers => 1,
                Eventually.ActiveTimers => 0,
                Eventually.ActiveTimerCountOfTwo or Eventually.AtLeastTwoActiveTimers => 1,
                Eventually.AtMostOneActiveTimer => 2,
                Eventually.TimerScheduledAtThirtySeconds => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(assertion), assertion, message: null),
            };
            for (var i = 0; i < unmet; i++)
            {
                _timers.Add(_time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan));
            }
        }

        /// <summary>Changes the provider so the condition holds.</summary>
        public void MakeMet()
        {
            switch (_assertion)
            {
                case Eventually.NoActiveTimers:
                case Eventually.AtMostOneActiveTimer:
                    _timers[0].Dispose();
                    break;
                case Eventually.ActiveTimers:
                case Eventually.ActiveTimerCountOfTwo:
                case Eventually.AtLeastTwoActiveTimers:
                    _timers.Add(_time.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan));
                    break;
                case Eventually.TimerScheduledAtThirtySeconds:
                    _ = _timers[0].Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                    break;
                default:
                    throw new UnreachableException();
            }
        }

        /// <summary>Runs the row's assertion with <paramref name="timeout"/>, or the token-only form when it is
        /// <see langword="null"/>.</summary>
        public async Task AssertAsync(TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var time = _time;
            switch (_assertion)
            {
                case Eventually.NoActiveTimers when timeout is { } bound:
                    await Assert.That(time).HasNoActiveTimersEventually(bound, cancellationToken);
                    break;
                case Eventually.NoActiveTimers:
                    await Assert.That(time).HasNoActiveTimersEventually(cancellationToken);
                    break;
                case Eventually.ActiveTimers when timeout is { } bound:
                    await Assert.That(time).HasActiveTimersEventually(bound, cancellationToken);
                    break;
                case Eventually.ActiveTimers:
                    await Assert.That(time).HasActiveTimersEventually(cancellationToken);
                    break;
                case Eventually.ActiveTimerCountOfTwo when timeout is { } bound:
                    await Assert.That(time).HasActiveTimerCountEventually(2, bound, cancellationToken);
                    break;
                case Eventually.ActiveTimerCountOfTwo:
                    await Assert.That(time).HasActiveTimerCountEventually(2, cancellationToken);
                    break;
                case Eventually.AtLeastTwoActiveTimers when timeout is { } bound:
                    await Assert.That(time).HasAtLeastActiveTimerCountEventually(2, bound, cancellationToken);
                    break;
                case Eventually.AtLeastTwoActiveTimers:
                    await Assert.That(time).HasAtLeastActiveTimerCountEventually(2, cancellationToken);
                    break;
                case Eventually.AtMostOneActiveTimer when timeout is { } bound:
                    await Assert.That(time).HasAtMostActiveTimerCountEventually(1, bound, cancellationToken);
                    break;
                case Eventually.AtMostOneActiveTimer:
                    await Assert.That(time).HasAtMostActiveTimerCountEventually(1, cancellationToken);
                    break;
                case Eventually.TimerScheduledAtThirtySeconds when timeout is { } bound:
                    await Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(30), bound, cancellationToken);
                    break;
                case Eventually.TimerScheduledAtThirtySeconds:
                    await Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(30), cancellationToken);
                    break;
                default:
                    throw new UnreachableException();
            }
        }
    }
}
