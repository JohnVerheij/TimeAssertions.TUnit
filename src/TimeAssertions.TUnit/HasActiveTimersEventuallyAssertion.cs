using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// TUnit assertion that polls an <see cref="ObservableTimeProvider"/> in real time until at least one
/// timer is active, or a timeout elapses. Generates the <c>HasActiveTimersEventually(TimeSpan, ...)</c>
/// chain extension via TUnit's <see cref="AssertionExtensionAttribute"/> source generator.
/// </summary>
/// <remarks>
/// <para>
/// The asynchronous counterpart of the synchronous <c>HasActiveTimers()</c> and a named shorthand for
/// <c>HasAtLeastActiveTimerCountEventually(1, ...)</c>: the natural shape for an asynchronous
/// registration wait that only needs "a timer eventually started" without pinning the exact count.
/// Use it when a loop registers its timer on a real continuation, so a synchronous
/// <c>HasActiveTimers()</c> would race the not-yet-registered timer.
/// </para>
/// <para>
/// The condition is checked once before the first delay, so a provider that already has a timer passes
/// without waiting.
/// </para>
/// <para>
/// <b>Cancellation.</b> When the supplied <see cref="CancellationToken"/> is canceled the poll loop
/// throws <see cref="OperationCanceledException"/> (a <see cref="TaskCanceledException"/> from the
/// underlying <see cref="Task.Delay(TimeSpan, CancellationToken)"/>); the test is recorded as
/// canceled rather than failed.
/// </para>
/// </remarks>
[AssertionExtension("HasActiveTimersEventually")]
public sealed class HasActiveTimersEventuallyAssertion : Assertion<ObservableTimeProvider>
{
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollingInterval;
    private readonly CancellationToken _cancellationToken;

    /// <summary>Initializes the assertion with a polling timeout, poll interval, and cancellation
    /// token. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for at least one timer to become
    /// active. Must be non-negative.</param>
    /// <param name="pollingInterval">The delay between polls. When <see langword="null"/> a default of
    /// <see cref="PollingDefaults.PollingInterval"/> is used. Must be positive when supplied.</param>
    /// <param name="cancellationToken">A token that cancels the poll loop.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, or
    /// <paramref name="pollingInterval"/> is supplied and not positive.</exception>
    public HasActiveTimersEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan timeout,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
        : base(context)
    {
        _timeout = PollingDefaults.ValidateTimeout(timeout);
        _pollingInterval = PollingDefaults.ResolvePollingInterval(pollingInterval);
        _cancellationToken = cancellationToken;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".HasActiveTimersEventually({TimeRenderingHelpers.FormatDuration(_timeout)})");
    }

    /// <inheritdoc/>
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<ObservableTimeProvider> metadata)
    {
        if (metadata.Exception is not null)
        {
            return PollingDefaults.FailFromSourceException(metadata.Exception);
        }

        var value = metadata.Value;
        if (value is null)
        {
            return AssertionResult.Failed("the observable time provider was null");
        }

        var reached = await PollingDefaults
            .PollUntilAsync(() => value.ActiveTimerCount >= 1, _timeout, _pollingInterval, _cancellationToken)
            .ConfigureAwait(false);

        if (reached)
        {
            return AssertionResult.Passed;
        }

        return AssertionResult.Failed(
            $"expected at least one active timer within {TimeRenderingHelpers.FormatDuration(_timeout)} but the active-timer count stayed at 0");
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return $"at least one active timer within {TimeRenderingHelpers.FormatDuration(_timeout)}";
    }
}
