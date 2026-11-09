using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.TUnit.Tests;

/// <summary>Parallel tests that share one clock through <see cref="SharedClockFixture"/>: each drives its own backoff
/// on the shared clock with <c>AdvanceOnEachTimerUntilAsync</c>, while the fixture's heartbeat and the other tests'
/// timers are on the same provider. Not a recommended pattern (the README advises a provider per loop): the helpers
/// then react to each other's timers and move the clock further than one loop needs, and this pins only that each
/// backoff still completes, without a stall.</summary>
[Timeout(15_000)]
internal sealed class SharedClockFixtureTests
{
    private static readonly SharedClockFixture Shared = new();

    /// <summary>Gets the shared fixture, which TUnit initializes before the first test that uses it.</summary>
    [MethodDataSource(nameof(TheSharedFixture))]
    public required SharedClockFixture Fixture { get; init; }

    /// <summary>Supplies the one shared fixture to every test.</summary>
    /// <returns>A factory that returns the shared fixture.</returns>
    public static Func<SharedClockFixture> TheSharedFixture() => static () => Shared;

    /// <summary>One of four parallel tests on the shared clock.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ParallelTestOnTheSharedClock_ItsBackoffCompletes(int failures, CancellationToken cancellationToken)
    {
        var attempts = await Fixture.Clock.AdvanceOnEachTimerUntilAsync(
            BackoffAsync(Fixture.Clock, failures, cancellationToken),
            TimeSpan.FromSeconds(30),
            cancellationToken);

        await Assert.That(attempts).IsEqualTo(failures + 1);
        await Assert.That(Fixture.Initializations).IsEqualTo(1);
    }

    private static async Task<int> BackoffAsync(TimeProvider time, int failures, CancellationToken cancellationToken)
    {
        await Task.Yield();
        for (var attempt = 1; attempt <= failures; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), time, cancellationToken).ConfigureAwait(false);
        }

        return failures + 1;
    }
}
