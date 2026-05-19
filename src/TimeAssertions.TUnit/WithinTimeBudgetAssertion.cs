using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// TUnit assertion that imposes a wall-clock timing budget on the preceding assertion source.
/// Generates the <c>WithinTimeBudget(TimeSpan)</c> chain extension via TUnit's
/// <see cref="AssertionExtensionAttribute"/> source generator.
/// </summary>
/// <typeparam name="T">The value type of the underlying assertion. Inferred at the call site
/// when chained via <c>.And.WithinTimeBudget(...)</c> after a behavioural assertion.</typeparam>
/// <remarks>
/// <para>
/// <b>Canonical chain pattern:</b> place <c>.WithinTimeBudget()</c> after <c>.And</c> on a
/// behavioural assertion. The <c>.And</c> continuation returns
/// <c>IAssertionSource&lt;T&gt;</c>, so the source generator's emitted
/// <c>WithinTimeBudget&lt;T&gt;(this IAssertionSource&lt;T&gt;, ...)</c> extension binds with
/// type inference:
/// </para>
/// <code>
/// await Assert.That(asyncOp)
///     .IsEqualTo(42)
///     .And.WithinTimeBudget(TimeSpan.FromMilliseconds(500));
/// </code>
/// <para>
/// <b>Post-facto check, NOT cancellation.</b> The wall-clock duration captured by TUnit's
/// <see cref="EvaluationMetadata{T}.Duration"/> is compared against the budget after the
/// assertion's evaluator has run; the assertion is not cancelled mid-flight. This decouples
/// TimeAssertions from TUnit's assertion lifecycle internals and avoids overlap with each
/// sibling package's domain-specific timeout API.
/// </para>
/// <para>
/// For polling / streaming workloads, use the package's own timeout API (e.g.
/// <c>LogAssertions.WithinTimeout</c>): <c>.WithinTimeBudget()</c> would let an unbounded
/// poll run and only flag the overrun at the end.
/// </para>
/// <para>
/// <b>External cancellation propagates intact.</b> When the wrapped operation's
/// <see cref="System.Threading.CancellationToken"/> cancels (parent <c>[Timeout]</c>,
/// test-class CT, runner cancel), the
/// <see cref="OperationCanceledException"/> propagates through this assertion to the
/// test runner via <see cref="ExceptionDispatchInfo"/> without being wrapped as an
/// assertion failure. The test is recorded as cancelled, not failed. Non-OCE source
/// exceptions continue to surface as <see cref="AssertionResult.Failed(string)"/>.
/// </para>
/// </remarks>
[AssertionExtension("WithinTimeBudget")]
public sealed class WithinTimeBudgetAssertion<T> : Assertion<T>
{
    private readonly TimeSpan _budget;

    /// <summary>Initialises the assertion with a wall-clock budget. Called by the TUnit source
    /// generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="budget">The maximum wall-clock duration the preceding assertion's evaluator
    /// is allowed to take.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative.</exception>
    public WithinTimeBudgetAssertion(AssertionContext<T> context, TimeSpan budget)
        : base(context)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero);
        _budget = budget;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".WithinTimeBudget({TimeRenderingHelpers.FormatDuration(budget)})");
    }

    /// <inheritdoc/>
    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<T> metadata)
    {
        // External cancellation (parent [Timeout], test-class CT, runner cancel) reaches
        // this assertion via metadata.Exception. We don't own a CancellationTokenSource here:
        // any OperationCanceledException came from outside, not from an internal timeout, so
        // re-throw it to the test runner so the test is recorded as cancelled rather than
        // assertion-failed. ExceptionDispatchInfo preserves the original stack trace.
        if (metadata.Exception is OperationCanceledException oce)
        {
            ExceptionDispatchInfo.Capture(oce).Throw();
        }

        // If the source threw any other exception, surface it via AssertionResult.Failed so
        // TUnit's normal pipeline re-raises it as AssertionException with full context.
        // Mirrors the pattern used by TUnit's own CompletesWithin assertions and our sibling
        // SnapshotAssertion: timing surface is additive, but a thrown source is the dominant
        // failure mode and should not be masked.
        if (metadata.Exception is not null)
        {
            return Task.FromResult(AssertionResult.Failed(
                $"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}",
                metadata.Exception));
        }

        var elapsed = metadata.Duration;
        if (elapsed > _budget)
        {
            return Task.FromResult(AssertionResult.Failed(TimeRenderingHelpers.FormatBudgetOverrun(elapsed, _budget)));
        }

        return Task.FromResult(AssertionResult.Passed);
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return $"completion within timing budget of {TimeRenderingHelpers.FormatDuration(_budget)}";
    }
}

