using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions.TUnit;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>End-to-end tests for the <c>.WithinTimeBudget()</c> extension. The TUnit
/// <c>[AssertionExtension]</c> source generator emits an extension on
/// <c>IAssertionSource&lt;T&gt;</c>. Two chain shapes work:
/// <list type="bullet">
/// <item><b>Canonical:</b> <c>Assert.That(x).IsEqualTo(...).And.WithinTimeBudget(TimeSpan)</c> — type inference works, no explicit <c>&lt;T&gt;</c> needed.</item>
/// <item>Direct-on-source: <c>Assert.That(asyncTask).WithinTimeBudget&lt;int&gt;(TimeSpan)</c> — requires explicit type argument because the Task-source's inferred type doesn't drive the generator-emitted overload.</item>
/// </list>
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class WithinTimeBudgetAssertionTests
{
    /// <summary>A fast async source comfortably under a generous budget passes.</summary>
    [Test]
    public async Task FastSource_WithGenerousBudget_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Direct-on-source path requires explicit type argument because Assert.That(Task<T>)'s
        // returned source type doesn't drive type inference for our extension. The canonical
        // pattern (.And.WithinTimeBudget) infers cleanly — see AndChain test.
        await Assert.That(FastSourceAsync(cancellationToken)).WithinTimeBudget<int>(TimeSpan.FromSeconds(5));
    }

    /// <summary>A slow async source exceeds a tight budget; <c>.WithinTimeBudget()</c> fails with a
    /// budget-overrun message that includes both the elapsed and budget values.</summary>
    [Test]
    public async Task SlowSource_WithTightBudget_Fails(CancellationToken cancellationToken)
    {
        var exception = await Assert.That(async () =>
        {
            await Assert.That(SlowSourceAsync(TimeSpan.FromMilliseconds(200), cancellationToken))
                .WithinTimeBudget<int>(TimeSpan.FromMilliseconds(50));
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("budget");
    }

    /// <summary>A source that throws does NOT mask its exception with a timing failure;
    /// the underlying exception propagates through TUnit's normal error pipeline. Timing
    /// surface is additive, not replacement.</summary>
    [Test]
    public async Task ThrowingSource_PropagatesUnderlyingException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(ThrowingSourceAsync(cancellationToken))
                .WithinTimeBudget<int>(TimeSpan.FromSeconds(5));
        }).Throws<AssertionException>();
    }

    /// <summary>Constructor argument validation: negative budget rejected.</summary>
    [Test]
    public async Task NegativeBudget_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(FastSourceAsync(cancellationToken))
                .WithinTimeBudget<int>(TimeSpan.FromMilliseconds(-1));
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>Zero budget: any non-trivially-zero elapsed time fails.</summary>
    [Test]
    public async Task ZeroBudget_NonZeroElapsed_Fails(CancellationToken cancellationToken)
    {
        await Assert.That(async () =>
        {
            await Assert.That(SlowSourceAsync(TimeSpan.FromMilliseconds(50), cancellationToken))
                .WithinTimeBudget<int>(TimeSpan.Zero);
        }).Throws<AssertionException>();
    }

    /// <summary>Pins that <c>.And.WithinTimeBudget()</c> works after an intermediate assertion.
    /// The <c>.And</c> continuation returns <c>IAssertionSource&lt;T&gt;</c>, which is the
    /// surface our <c>[AssertionExtension]</c>-emitted extension targets — so timing budgets
    /// CAN compose with behavioural assertion chains, just via <c>.And</c> rather than directly.</summary>
    [Test]
    public async Task AndChain_AfterIntermediateAssertion_Composes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(FastSourceAsync(cancellationToken))
            .IsEqualTo(42)
            .And.WithinTimeBudget(TimeSpan.FromSeconds(5));
    }

    /// <summary>Microseconds-band budget: the source assertion still passes (TUnit's
    /// <c>EvaluationMetadata.Duration</c> is captured at sub-millisecond precision, so
    /// <c>FastSourceAsync</c>'s <c>Task.Yield()</c> alone may exceed 100μs and trigger
    /// budget overrun). The point of this test is not the pass/fail outcome but the side
    /// effect: <c>WithinTimeBudgetAssertion</c>'s constructor and <c>GetExpectation</c>
    /// call <c>TimeRenderingHelpers.FormatDuration</c> on the budget, exercising the
    /// microseconds-band code path of <c>FormatDuration</c> that the direct tests in
    /// <c>TimeAssertions.Tests</c> cover (those run in the no-coverage CI step). This test
    /// keeps that band exercised in the coverage-instrumented test exe.</summary>
    [Test]
    public async Task MicrosecondsBudget_ExercisesFormatDurationMicroBand(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(SlowSourceAsync(TimeSpan.FromMilliseconds(20), cancellationToken))
                .WithinTimeBudget<int>(TimeSpan.FromMicroseconds(100));
        }).Throws<AssertionException>();
    }

    /// <summary>Minutes:seconds-band budget: a 90-second budget is far above any realistic
    /// wall-clock duration in this test, so the assertion passes. The constructor and
    /// <c>GetExpectation</c> calls of <c>WithinTimeBudgetAssertion</c> exercise
    /// <c>TimeRenderingHelpers.FormatDuration</c>'s minutes:seconds-band code path
    /// (formats as "1:30") in the coverage-instrumented test exe.</summary>
    [Test]
    public async Task MinutesSecondsBudget_ExercisesFormatDurationMinSecBand(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(FastSourceAsync(cancellationToken))
            .IsEqualTo(42)
            .And.WithinTimeBudget(TimeSpan.FromSeconds(90));
    }

    private static async Task<int> FastSourceAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return 42;
    }

    private static async Task<int> SlowSourceAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        return 42;
    }

    // ---- WithinTimeBudgetCapturing tests ----

    /// <summary>Capturing variant on a fast source: budget passes AND the capture callback
    /// receives a non-negative elapsed within the budget.</summary>
    [Test]
    public async Task Capturing_FastSource_InvokesCaptureWithMeasuredElapsed(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = TimeSpan.Zero;
        // Sentinel: prove the callback fired. Without this, the captured-default Zero would
        // satisfy `>= Zero` even if the callback never ran, so the test would silently lose
        // its protection against a regression where WithinTimeBudgetCapturing forgets to invoke.
        var captureInvoked = false;
        await Assert.That(FastSourceAsync(cancellationToken))
            .IsEqualTo(42)
            .And.WithinTimeBudgetCapturing(TimeSpan.FromSeconds(5), e => { captured = e; captureInvoked = true; });

        await Assert.That(captureInvoked).IsTrue();
        await Assert.That(captured).IsGreaterThanOrEqualTo(TimeSpan.Zero);
        await Assert.That(captured).IsLessThan(TimeSpan.FromSeconds(5));
    }

    /// <summary>Capturing on a slow source that overruns the budget still invokes capture
    /// before propagating the assertion failure — consumers should see the actual elapsed
    /// in their failure diagnostic.</summary>
    [Test]
    public async Task Capturing_SlowSource_CaptureInvokedBeforeFailure(CancellationToken cancellationToken)
    {
        var captured = TimeSpan.Zero;
        await Assert.That(async () =>
        {
            await Assert.That(SlowSourceAsync(TimeSpan.FromMilliseconds(200), cancellationToken))
                .IsEqualTo(42)
                .And.WithinTimeBudgetCapturing(TimeSpan.FromMilliseconds(50), e => captured = e);
        }).Throws<AssertionException>();

        // Capture must have run with a non-zero observed duration despite the overrun.
        await Assert.That(captured).IsGreaterThan(TimeSpan.Zero);
    }

    /// <summary>If the source throws, capture is still invoked. Underlying exception still
    /// propagates as <see cref="AssertionException"/>.</summary>
    [Test]
    public async Task Capturing_ThrowingSource_CaptureStillInvoked(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captureRan = false;
        await Assert.That(async () =>
        {
            await Assert.That(ThrowingSourceAsync(cancellationToken))
                .WithinTimeBudgetCapturing<int>(TimeSpan.FromSeconds(5), _ => captureRan = true);
        }).Throws<AssertionException>();

        await Assert.That(captureRan).IsTrue();
    }

    /// <summary>Constructor argument validation: negative budget rejected.</summary>
    [Test]
    public async Task Capturing_NegativeBudget_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(FastSourceAsync(cancellationToken))
                .WithinTimeBudgetCapturing<int>(TimeSpan.FromMilliseconds(-1), _ => { });
        }).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>Constructor argument validation: null capture rejected.</summary>
    [Test]
    public async Task Capturing_NullCapture_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(FastSourceAsync(cancellationToken))
                .WithinTimeBudgetCapturing<int>(TimeSpan.FromSeconds(5), capture: null!);
        }).Throws<ArgumentNullException>();
    }

    private static async Task<int> ThrowingSourceAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("source threw");
    }
}
