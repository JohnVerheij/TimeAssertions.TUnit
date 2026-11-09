using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Core.Interfaces;

namespace TimeAssertions.TUnit.Tests;

/// <summary>A clock shared by several tests through a fixture: a heartbeat loop is started in
/// <c>InitializeAsync</c>, and <c>DisposeAsync</c> stops it and waits until its timer is gone.</summary>
internal sealed class SharedClockFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private int _initializations;

    /// <summary>Gets the shared clock.</summary>
    public ObservableFakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Gets how many times TUnit initialized the fixture.</summary>
    public int Initializations => Volatile.Read(ref _initializations);

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        _ = Interlocked.Increment(ref _initializations);
        _ = HeartbeatAsync(Clock, _stop.Token);
        await Clock.WaitForTimersCreatedAsync(1, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await Assert.That(Clock).HasNoActiveTimersEventually(TimeSpan.FromSeconds(2), CancellationToken.None);
        _stop.Dispose();
    }

    /// <summary>The fixture's background loop: 30-second delays until <paramref name="stop"/> is canceled.</summary>
    private static async Task HeartbeatAsync(TimeProvider time, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), time, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped by the fixture.
        }
    }
}
