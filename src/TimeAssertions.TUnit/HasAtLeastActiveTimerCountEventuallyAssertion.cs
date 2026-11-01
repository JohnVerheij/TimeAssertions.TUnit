using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// TUnit assertion that waits until the active-timer count reaches at least the expected count on an <see cref="ObservableTimeProvider"/>. Generates the
/// <c>HasAtLeastActiveTimerCountEventually</c> chain extensions via TUnit's <see cref="AssertionExtensionAttribute"/> source generator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Event-driven.</b> The condition is checked once, then again each time a timer is created,
/// re-scheduled, fired, or disposed through the provider (<see cref="ObservableTimeProvider.WaitUntilAsync"/>),
/// never on a polling loop. A timer that a <c>BackgroundService</c> disposes on a continuation after
/// <c>StopAsync</c> returns is seen the moment the disposal happens.
/// </para>
/// <para>
/// <b>Bounds.</b> The <c>(timeout, cancellationToken)</c> form fails when <c>timeout</c> of real time passes
/// first; the timeout is a bound, not a poll interval. The <c>(cancellationToken)</c> form is bounded by the
/// token only, typically the test's own.
/// </para>
/// <para>
/// <b>Cancellation.</b> When the token is canceled the wait throws <see cref="OperationCanceledException"/> and
/// the test is recorded as canceled rather than failed. For a failure that describes the timers, pass a
/// timeout shorter than the test's own timeout.
/// </para>
/// <code>
/// await Assert.That(time).HasAtLeastActiveTimerCountEventually(3, ct);
/// </code>
/// </remarks>
[AssertionExtension("HasAtLeastActiveTimerCountEventually")]
public sealed class HasAtLeastActiveTimerCountEventuallyAssertion : Assertion<ObservableTimeProvider>
{
    private readonly int _count;
    private readonly TimeSpan? _timeout;
    private readonly CancellationToken _cancellationToken;

    /// <summary>The bound for messages: " within &lt;timeout&gt;", or empty for the token-only form.</summary>
    private readonly string _within;

    /// <summary>Initializes the assertion with a real-time bound and a cancellation token. Called by the
    /// TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="count">The minimum active-timer count to wait for. Must be non-negative.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative and at most about 49.7 days (<c>uint.MaxValue - 1</c> milliseconds).</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative or longer than about 49.7 days, or <paramref name="count"/> is negative.</exception>
    public HasAtLeastActiveTimerCountEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        : this(context, count, TimerWait.ValidateTimeout(timeout), cancellationToken)
    {
    }

    /// <summary>Initializes the assertion bounded by <paramref name="cancellationToken"/> only. Called by the
    /// TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="count">The minimum active-timer count to wait for. Must be non-negative.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public HasAtLeastActiveTimerCountEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        int count,
        CancellationToken cancellationToken)
        : this(context, count, default(TimeSpan?), cancellationToken)
    {
    }

    private HasAtLeastActiveTimerCountEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        int count,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        : base(context)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;
        _timeout = timeout;
        _within = timeout is { } bound ? " within " + TimeRenderingHelpers.FormatDuration(bound) : string.Empty;
        _cancellationToken = cancellationToken;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".HasAtLeastActiveTimerCountEventually({_count}, {(_timeout is { } timeoutValue ? TimeRenderingHelpers.FormatDuration(timeoutValue) : "cancellationToken")})");
    }

    /// <inheritdoc/>
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<ObservableTimeProvider> metadata)
    {
        if (metadata.Exception is not null)
        {
            return TimerWait.FailFromSourceException(metadata.Exception);
        }

        var value = metadata.Value;
        if (value is null)
        {
            return AssertionResult.Failed("the observable time provider was null");
        }

        var reached = await TimerWait
            .UntilAsync(value, provider => provider.ActiveTimerCount >= _count, _timeout, _cancellationToken)
            .ConfigureAwait(false);

        if (reached)
        {
            return AssertionResult.Passed;
        }

        var active = value.ActiveTimers;
        return AssertionResult.Failed(
            string.Create(
                CultureInfo.InvariantCulture,
                $"expected the active-timer count to reach at least {_count}{_within} but it was {active.Count} (minimum={_count}, actual={active.Count}):")
            + TimeRenderingHelpers.FormatActiveTimerSurvivors(active));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return string.Create(CultureInfo.InvariantCulture, $"at least {_count} active timer(s){_within}");
    }
}
