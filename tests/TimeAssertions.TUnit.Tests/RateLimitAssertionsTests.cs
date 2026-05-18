using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions.TUnit;
using TUnit.Assertions.Exceptions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>End-to-end tests for the <c>.WasInvokedAtMostOncePer(...)</c> assertion. The TUnit
/// <c>[GenerateAssertion]</c> source generator emits an extension on
/// <c>IAssertionSource&lt;IReadOnlyList&lt;DateTimeOffset&gt;&gt;</c>. The receiver is the
/// recorded invocation log; the assertion examines consecutive-pair gaps and fails on the
/// first interval below the configured minimum.</summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class RateLimitAssertionsTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Empty sequence passes: no consecutive pair exists to violate.</summary>
    [Test]
    public async Task EmptySequence_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DateTimeOffset> timestamps = Array.Empty<DateTimeOffset>();
        await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
    }

    /// <summary>Single-element sequence passes: no consecutive pair exists to violate.</summary>
    [Test]
    public async Task SingleElement_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DateTimeOffset> timestamps = new[] { Epoch };
        await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
    }

    /// <summary>Two invocations beyond the required interval: passes.</summary>
    [Test]
    public async Task TwoBeyondInterval_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DateTimeOffset> timestamps = new[]
        {
            Epoch,
            Epoch + TimeSpan.FromSeconds(60),
        };
        await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
    }

    /// <summary>Boundary case: the gap is exactly equal to the required interval.
    /// The minimum is inclusive, so this passes.</summary>
    [Test]
    public async Task TwoExactlyAtInterval_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DateTimeOffset> timestamps = new[]
        {
            Epoch,
            Epoch + TimeSpan.FromSeconds(30),
        };
        await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
    }

    /// <summary>Two invocations within the interval: fails. Message names the violating
    /// index (1), observed gap, and required minimum.</summary>
    [Test]
    public async Task TwoWithinInterval_FailsWithMessage(CancellationToken cancellationToken)
    {
        IReadOnlyList<DateTimeOffset> timestamps = new[]
        {
            Epoch,
            Epoch + TimeSpan.FromSeconds(5),
        };

        var exception = await Assert.That(async () =>
        {
            await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("interval violation at index 1");
        await Assert.That(exception.Message).Contains("(gap=5000ms, minimum=30000ms)");
    }

    /// <summary>Burst followed by quiet: the FIRST violating burst is reported, not the
    /// later quiet intervals. Matches the family "find the first violation" convention.</summary>
    [Test]
    public async Task BurstThenQuiet_FailsAtFirstBurst(CancellationToken cancellationToken)
    {
        IReadOnlyList<DateTimeOffset> timestamps = new[]
        {
            Epoch,
            Epoch + TimeSpan.FromSeconds(5),
            Epoch + TimeSpan.FromSeconds(60),
            Epoch + TimeSpan.FromSeconds(120),
        };

        var exception = await Assert.That(async () =>
        {
            await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromSeconds(30));
        }).Throws<AssertionException>();

        await Assert.That(exception!.Message).Contains("interval violation at index 1");
    }

    /// <summary>Zero interval: any strictly-ascending sequence passes (every gap is at
    /// least zero). Duplicate-timestamp pairs would fail because their gap is zero, which
    /// is not strictly less than zero but the check is <c>gap &lt; interval</c> so
    /// <c>gap == 0</c> with <c>interval == 0</c> is inclusive-pass.</summary>
    [Test]
    public async Task ZeroInterval_StrictlyAscending_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DateTimeOffset> timestamps = new[]
        {
            Epoch,
            Epoch + TimeSpan.FromMilliseconds(1),
            Epoch + TimeSpan.FromMilliseconds(2),
        };
        await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.Zero);
    }

    /// <summary>Argument validation: negative interval rejected with
    /// <see cref="ArgumentOutOfRangeException"/>.</summary>
    [Test]
    public async Task NegativeInterval_ThrowsArgumentOutOfRange(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DateTimeOffset> timestamps = new[] { Epoch, Epoch + TimeSpan.FromSeconds(60) };
        await Assert.That(async () =>
        {
            await Assert.That(timestamps).WasInvokedAtMostOncePer(TimeSpan.FromMilliseconds(-1));
        }).Throws<ArgumentOutOfRangeException>();
    }
}
