using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// TUnit assertion that polls an <see cref="ObservableTimeProvider"/> in real time until its active
/// timer count is at most an expected upper bound, or a timeout elapses. Generates the
/// <c>HasAtMostActiveTimerCountEventually(int, TimeSpan, ...)</c> chain extension via TUnit's
/// <see cref="AssertionExtensionAttribute"/> source generator.
/// </summary>
/// <remarks>
/// <para>
/// The asynchronous upper-bound sibling of <see cref="HasAtLeastActiveTimerCountEventuallyAssertion"/>.
/// Use it when timers dispose on real continuations and the expectation is that the active set settles
/// to no more than <c>count</c> shortly after the observed call returns, without pinning
/// the exact survivor count. <c>HasAtMostActiveTimerCountEventually(0)</c> is equivalent to
/// <c>HasNoActiveTimersEventually</c>.
/// </para>
/// <para>
/// The condition is checked once before the first delay, so an already-satisfied provider passes
/// without waiting.
/// </para>
/// <para>
/// <b>Cancellation.</b> When the supplied <see cref="CancellationToken"/> is canceled the poll loop
/// throws <see cref="OperationCanceledException"/> (a <see cref="TaskCanceledException"/> from the
/// underlying <see cref="Task.Delay(TimeSpan, CancellationToken)"/>); the test is recorded as
/// canceled rather than failed.
/// </para>
/// </remarks>
[AssertionExtension("HasAtMostActiveTimerCountEventually")]
public sealed class HasAtMostActiveTimerCountEventuallyAssertion : Assertion<ObservableTimeProvider>
{
    private readonly int _count;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollingInterval;
    private readonly CancellationToken _cancellationToken;

    /// <summary>Initializes the assertion with the maximum count, a polling timeout, poll interval,
    /// and cancellation token. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="count">The maximum active-timer count to poll for. Must be non-negative.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for the active-timer count to fall to
    /// at most <paramref name="count"/>. Must be non-negative.</param>
    /// <param name="pollingInterval">The delay between polls. When <see langword="null"/> a default of
    /// <see cref="PollingDefaults.PollingInterval"/> is used. Must be positive when supplied.</param>
    /// <param name="cancellationToken">A token that cancels the poll loop.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative,
    /// <paramref name="timeout"/> is negative, or <paramref name="pollingInterval"/> is supplied and not
    /// positive.</exception>
    public HasAtMostActiveTimerCountEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        int count,
        TimeSpan timeout,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
        : base(context)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;
        _timeout = PollingDefaults.ValidateTimeout(timeout);
        _pollingInterval = PollingDefaults.ResolvePollingInterval(pollingInterval);
        _cancellationToken = cancellationToken;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".HasAtMostActiveTimerCountEventually({_count}, {TimeRenderingHelpers.FormatDuration(_timeout)})");
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
            .PollUntilAsync(() => value.ActiveTimerCount <= _count, _timeout, _pollingInterval, _cancellationToken)
            .ConfigureAwait(false);

        if (reached)
        {
            return AssertionResult.Passed;
        }

        var active = value.ActiveTimers;
        return AssertionResult.Failed(
            string.Create(
                CultureInfo.InvariantCulture,
                $"expected the active-timer count to fall to at most {_count} within {TimeRenderingHelpers.FormatDuration(_timeout)} but it was {active.Count} (maximum={_count}, actual={active.Count}):")
            + TimeRenderingHelpers.FormatActiveTimerSurvivors(active));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"at most {_count} active timer(s) within {TimeRenderingHelpers.FormatDuration(_timeout)}");
    }
}
