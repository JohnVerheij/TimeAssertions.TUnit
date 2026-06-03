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
/// timer count equals an expected value, or a timeout elapses. Generates the
/// <c>HasActiveTimerCountEventually(int, TimeSpan, ...)</c> chain extension via TUnit's
/// <see cref="AssertionExtensionAttribute"/> source generator.
/// </summary>
/// <remarks>
/// <para>
/// The count-targeted sibling of <see cref="HasNoActiveTimersEventuallyAssertion"/>. Use it when a
/// timer is expected to be created (or disposed down to a steady-state count) on a real asynchronous
/// continuation rather than synchronously: the active set settles to <c>count</c> shortly after the
/// observed call returns. Polls the live <see cref="ObservableTimeProvider.ActiveTimerCount"/>
/// against a wall-clock deadline; the condition is checked once before the first delay.
/// </para>
/// <para>
/// <b>Cancellation.</b> When the supplied <see cref="CancellationToken"/> is canceled the poll loop
/// throws <see cref="OperationCanceledException"/> (a <see cref="TaskCanceledException"/> from the
/// underlying <see cref="Task.Delay(TimeSpan, CancellationToken)"/>); the test is recorded as
/// canceled rather than failed.
/// </para>
/// </remarks>
[AssertionExtension("HasActiveTimerCountEventually")]
public sealed class HasActiveTimerCountEventuallyAssertion : Assertion<ObservableTimeProvider>
{
    private readonly int _count;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollingInterval;
    private readonly CancellationToken _ct;

    /// <summary>Initializes the assertion with the expected count, a polling timeout, poll interval,
    /// and cancellation token. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="count">The active-timer count to poll for. Must be non-negative.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for the active-timer count to reach
    /// <paramref name="count"/>. Must be non-negative.</param>
    /// <param name="pollingInterval">The delay between polls. When <see langword="null"/> a default of
    /// <see cref="PollingDefaults.PollingInterval"/> is used. Must be positive when supplied.</param>
    /// <param name="ct">A token that cancels the poll loop.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative,
    /// <paramref name="timeout"/> is negative, or <paramref name="pollingInterval"/> is supplied and not
    /// positive.</exception>
    public HasActiveTimerCountEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        int count,
        TimeSpan timeout,
        TimeSpan? pollingInterval = null,
        CancellationToken ct = default)
        : base(context)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;
        _timeout = PollingDefaults.ValidateTimeout(timeout);
        _pollingInterval = PollingDefaults.ResolvePollingInterval(pollingInterval);
        _ct = ct;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".HasActiveTimerCountEventually({_count}, {TimeRenderingHelpers.FormatDuration(_timeout)})");
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
            .PollUntilAsync(() => value.ActiveTimerCount == _count, _timeout, _pollingInterval, _ct)
            .ConfigureAwait(false);

        if (reached)
        {
            return AssertionResult.Passed;
        }

        var active = value.ActiveTimers;
        return AssertionResult.Failed(
            string.Create(
                CultureInfo.InvariantCulture,
                $"expected the active-timer count to reach {_count} within {TimeRenderingHelpers.FormatDuration(_timeout)} but it was {active.Count} (expected={_count}, actual={active.Count}):")
            + TimeRenderingHelpers.FormatActiveTimerSurvivors(active));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_count} active timer(s) within {TimeRenderingHelpers.FormatDuration(_timeout)}");
    }
}
