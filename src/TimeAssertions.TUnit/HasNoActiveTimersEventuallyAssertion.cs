using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// TUnit assertion that waits until no timer remains active on an <see cref="ObservableTimeProvider"/>. Generates the
/// <c>HasNoActiveTimersEventually</c> chain extensions via TUnit's <see cref="AssertionExtensionAttribute"/> source generator.
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
/// await host.StopAsync(ct);
/// await Assert.That(time).HasNoActiveTimersEventually(TimeSpan.FromSeconds(2), ct);
/// </code>
/// </remarks>
[AssertionExtension("HasNoActiveTimersEventually")]
public sealed class HasNoActiveTimersEventuallyAssertion : Assertion<ObservableTimeProvider>
{
    private readonly TimeSpan? _timeout;
    private readonly CancellationToken _cancellationToken;

    /// <summary>The bound for messages: " within &lt;timeout&gt;", or empty for the token-only form.</summary>
    private readonly string _within;

    /// <summary>Initializes the assertion with a real-time bound and a cancellation token. Called by the
    /// TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative and at most about 49.7 days (<c>uint.MaxValue - 1</c> milliseconds).</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative or longer than about 49.7 days.</exception>
    public HasNoActiveTimersEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        : this(context, TimerWait.ValidateTimeout(timeout), cancellationToken)
    {
    }

    /// <summary>Initializes the assertion bounded by <paramref name="cancellationToken"/> only. Called by the
    /// TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    public HasNoActiveTimersEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        CancellationToken cancellationToken)
        : this(context, default(TimeSpan?), cancellationToken)
    {
    }

    private HasNoActiveTimersEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        : base(context)
    {
        _timeout = timeout;
        _within = timeout is { } bound ? " within " + TimeRenderingHelpers.FormatDuration(bound) : string.Empty;
        _cancellationToken = cancellationToken;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".HasNoActiveTimersEventually({(_timeout is { } timeoutValue ? TimeRenderingHelpers.FormatDuration(timeoutValue) : "cancellationToken")})");
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
            .UntilAsync(value, static provider => provider.ActiveTimerCount is 0, _timeout, _cancellationToken)
            .ConfigureAwait(false);

        if (reached)
        {
            return AssertionResult.Passed;
        }

        var survivors = value.ActiveTimers;
        return AssertionResult.Failed(
            string.Create(
                CultureInfo.InvariantCulture,
                $"expected the active-timer count to reach 0{_within} but {survivors.Count} timer(s) remained:")
            + TimeRenderingHelpers.FormatActiveTimerSurvivors(survivors));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return $"no active timers{_within}";
    }
}
