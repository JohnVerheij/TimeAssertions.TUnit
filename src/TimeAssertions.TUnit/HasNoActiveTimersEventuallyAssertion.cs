using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// TUnit assertion that polls an <see cref="ObservableTimeProvider"/> in real time until no timer
/// remains active, or a timeout elapses. Generates the
/// <c>HasNoActiveTimersEventually(TimeSpan, ...)</c> chain extension via TUnit's
/// <see cref="AssertionExtensionAttribute"/> source generator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a real-time poll rather than fake-time advance.</b> A <c>BackgroundService</c> /
/// <c>IHostedService</c> commonly disposes its timer on a continuation that runs <em>after</em>
/// <c>StopAsync</c> returns to the caller. The disposal happens on a real asynchronous
/// continuation, not on a fake-clock tick, so advancing fake time would not surface it. This
/// assertion therefore polls the live <see cref="ObservableTimeProvider.ActiveTimerCount"/> against
/// a wall-clock deadline, giving the pending disposal continuation time to run.
/// </para>
/// <para>
/// The condition is checked once before the first delay, so a provider that is already clean passes
/// without waiting. On timeout the failure message names each surviving timer by the schedule it
/// carries (<c>[dueTime=..., period=...]</c>), the same shape as the synchronous
/// <c>HasNoActiveTimers()</c> check.
/// </para>
/// <para>
/// <b>Cancellation.</b> When the supplied <see cref="CancellationToken"/> is canceled the poll loop
/// throws <see cref="OperationCanceledException"/> (a <see cref="TaskCanceledException"/> from the
/// underlying <see cref="Task.Delay(TimeSpan, CancellationToken)"/>); the test is recorded as
/// canceled rather than failed.
/// </para>
/// <code>
/// await host.StopAsync();
/// await Assert.That(time).HasNoActiveTimersEventually(TimeSpan.FromSeconds(2));
/// </code>
/// </remarks>
[AssertionExtension("HasNoActiveTimersEventually")]
public sealed class HasNoActiveTimersEventuallyAssertion : Assertion<ObservableTimeProvider>
{
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollingInterval;
    private readonly CancellationToken _ct;

    /// <summary>Initializes the assertion with a polling timeout, poll interval, and cancellation
    /// token. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for the active-timer count to reach
    /// zero. Must be non-negative.</param>
    /// <param name="pollingInterval">The delay between polls. When <see langword="null"/> a default of
    /// <see cref="PollingDefaults.PollingInterval"/> is used. Must be positive when supplied.</param>
    /// <param name="ct">A token that cancels the poll loop.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, or
    /// <paramref name="pollingInterval"/> is supplied and not positive.</exception>
    public HasNoActiveTimersEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan timeout,
        TimeSpan? pollingInterval = null,
        CancellationToken ct = default)
        : base(context)
    {
        _timeout = PollingDefaults.ValidateTimeout(timeout);
        _pollingInterval = PollingDefaults.ResolvePollingInterval(pollingInterval);
        _ct = ct;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".HasNoActiveTimersEventually({TimeRenderingHelpers.FormatDuration(_timeout)})");
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
            .PollUntilAsync(() => value.ActiveTimerCount is 0, _timeout, _pollingInterval, _ct)
            .ConfigureAwait(false);

        if (reached)
        {
            return AssertionResult.Passed;
        }

        var survivors = value.ActiveTimers;
        return AssertionResult.Failed(
            string.Create(
                CultureInfo.InvariantCulture,
                $"expected the active-timer count to reach 0 within {TimeRenderingHelpers.FormatDuration(_timeout)} but {survivors.Count} timer(s) remained:")
            + TimeRenderingHelpers.FormatActiveTimerSurvivors(survivors));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return $"no active timers within {TimeRenderingHelpers.FormatDuration(_timeout)}";
    }
}
