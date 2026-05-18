using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// Capturing variant of <see cref="WithinTimeBudgetAssertion{T}"/>. Same wall-clock budget
/// behaviour, but additionally invokes the supplied <c>capture</c> callback with the measured
/// elapsed duration regardless of whether the budget was met. Useful for tests that need to
/// log the observed timing or feed it into a follow-up <c>HasAdvancedApproximately</c> check.
/// </summary>
/// <typeparam name="T">The value type of the underlying assertion.</typeparam>
/// <remarks>
/// <para>The capture callback runs even when the budget is exceeded, so a test can still
/// surface the observed duration in its failure diagnostic before the budget-overrun
/// assertion exception propagates.</para>
/// <para>
/// <b>External cancellation propagates intact AND skips the capture callback.</b> When the
/// wrapped operation's <see cref="System.Threading.CancellationToken"/> cancels, the
/// <see cref="OperationCanceledException"/> is re-thrown via
/// <see cref="ExceptionDispatchInfo"/> so the test is recorded as cancelled, not failed.
/// The capture callback is NOT invoked on cancellation: a partial elapsed time from a
/// cancelled operation would mislead consumers since the operation did not complete
/// normally. Non-OCE source exceptions still invoke the capture (the elapsed duration is
/// meaningful in that case).
/// </para>
/// <para>Composes via <c>.And</c> like the non-capturing variant:</para>
/// <code>
/// var elapsed = TimeSpan.Zero;
/// await Assert.That(asyncOp)
///     .IsEqualTo(42)
///     .And.WithinTimeBudgetCapturing(TimeSpan.FromMilliseconds(500), e =&gt; elapsed = e);
/// // 'elapsed' now holds the wall-clock duration of the asyncOp evaluator.
/// </code>
/// </remarks>
[AssertionExtension("WithinTimeBudgetCapturing")]
public sealed class WithinTimeBudgetCapturingAssertion<T> : Assertion<T>
{
    private readonly TimeSpan _budget;
    private readonly Action<TimeSpan> _capture;

    /// <summary>Initialises the capturing assertion with a wall-clock budget and an elapsed
    /// callback. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="budget">The maximum wall-clock duration the preceding assertion's evaluator
    /// is allowed to take.</param>
    /// <param name="capture">Callback invoked with the measured elapsed duration. Called once
    /// per assertion evaluation, including when the budget is exceeded.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="capture"/> is <see langword="null"/>.</exception>
    public WithinTimeBudgetCapturingAssertion(AssertionContext<T> context, TimeSpan budget, Action<TimeSpan> capture)
        : base(context)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(capture);
        _budget = budget;
        _capture = capture;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".WithinTimeBudgetCapturing({TimeRenderingHelpers.FormatDuration(budget)})");
    }

    /// <inheritdoc/>
    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<T> metadata)
    {
        // External cancellation (parent [Timeout], test-class CT, runner cancel) reaches
        // this assertion via metadata.Exception. We don't own a CancellationTokenSource here:
        // any OperationCanceledException came from outside, not from an internal timeout, so
        // re-throw it to the test runner so the test is recorded as cancelled rather than
        // assertion-failed. The capture callback is NOT invoked: a partial elapsed from a
        // cancelled operation would mislead consumers about the operation's real cost.
        // ExceptionDispatchInfo preserves the original stack trace.
        if (metadata.Exception is OperationCanceledException oce)
        {
            ExceptionDispatchInfo.Capture(oce).Throw();
        }

        if (metadata.Exception is not null)
        {
            // Source threw a non-OCE exception: capture is still invoked with the partial
            // elapsed (TUnit reports metadata.Duration for thrown evaluators too) so consumers
            // see consistent capture semantics regardless of pass / fail / throw.
            _capture(metadata.Duration);
            return Task.FromResult(AssertionResult.Failed(
                $"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}",
                metadata.Exception));
        }

        var elapsed = metadata.Duration;
        _capture(elapsed);
        if (elapsed > _budget)
        {
            return Task.FromResult(AssertionResult.Failed(TimeRenderingHelpers.FormatBudgetOverrun(elapsed, _budget)));
        }

        return Task.FromResult(AssertionResult.Passed);
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return $"completion within timing budget of {TimeRenderingHelpers.FormatDuration(_budget)} (capturing elapsed)";
    }
}
