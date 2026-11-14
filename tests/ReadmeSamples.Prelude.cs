// What the README samples use without declaring it: the namespaces a test project imports, the reader's own
// types and test data. CI compiles the samples with this file (assertions-ci, README code samples); it is not
// part of any project here.
global using Microsoft.Extensions.Time.Testing;
global using TimeAssertions;
global using TimeAssertions.TUnit;

namespace ReadmeSamples;

/// <summary>
/// The reader's service that expires state after a delay (the quick start and the entry-point samples).
/// It is the base of <see cref="HeartbeatService"/> so that the one <c>service</c> field of the prelude serves
/// every sample that uses the name.
/// </summary>
public class ExpirationService(TimeProvider timeProvider)
{
    /// <summary>The time provider the service was given.</summary>
    protected TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>When the service last processed.</summary>
    public DateTimeOffset? LastProcessedAt { get; private set; }

    /// <summary>When the service runs next.</summary>
    public DateTimeOffset NextRunAt => TimeProvider.GetUtcNow().AddMinutes(30);

    /// <summary>Processes once.</summary>
    public Task ProcessAsync(CancellationToken cancellationToken)
    {
        LastProcessedAt = TimeProvider.GetUtcNow();
        return Task.CompletedTask;
    }
}

/// <summary>The reader's hosted service with a heartbeat timer (the timer samples).</summary>
public sealed class HeartbeatService : ExpirationService
{
    /// <summary>Creates the service on a time provider.</summary>
    public HeartbeatService(TimeProvider timeProvider)
        : base(timeProvider)
    {
    }

    /// <summary>Creates the service on a time provider, reporting each beat to a callback.</summary>
    public HeartbeatService(TimeProvider timeProvider, Action<string> callback)
        : base(timeProvider)
    {
        _ = callback;
    }

    /// <summary>The number of beats so far.</summary>
    public int Beats { get; private set; }

    /// <summary>Starts the heartbeat.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Beats = 0;
        return Task.CompletedTask;
    }

    /// <summary>Stops the heartbeat.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The reader's client that reconnects with a backoff (the backoff samples).</summary>
public sealed class ReconnectingClient(TimeProvider timeProvider)
{
    /// <summary>Whether the client is connected.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Reports a disconnect, which schedules the first reconnect attempt.</summary>
    public Task OnDisconnect() => Task.CompletedTask;

    /// <summary>Connects, waiting between attempts.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
        IsConnected = true;
    }
}

/// <summary>The exception the reader's client throws when it gives up.</summary>
public sealed class GiveUpException : Exception
{
}

/// <summary>A record of the reader's with an expiry (the <c>IsBeforeNow</c> sample).</summary>
public sealed class ExpiringRecord
{
    /// <summary>When the record expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>The test data the samples use without declaring it; every sample class derives from it.</summary>
public partial class Prelude
{
    /// <summary>The test's own token.</summary>
    protected CancellationToken cancellationToken;

    /// <summary>A fake clock.</summary>
    protected FakeTimeProvider fakeTime = new();

    /// <summary>An observable fake clock (<c>AdvanceOnEachTimerUntilAsync</c> exists only there).</summary>
    protected ObservableFakeTimeProvider time = new();

    /// <summary>An expected moment.</summary>
    protected DateTimeOffset expected = DateTimeOffset.UnixEpoch;

    /// <summary>A timestamp under test.</summary>
    protected DateTimeOffset timestamp = DateTimeOffset.UnixEpoch;

    /// <summary>The service under test.</summary>
    protected HeartbeatService service = new(TimeProvider.System);

    /// <summary>A record with an expiry.</summary>
    protected ExpiringRecord record = new();

    /// <summary>An operation under test and the value it should produce.</summary>
    protected Task<int> asyncOp = Task.FromResult(0);

    /// <summary>The value <see cref="asyncOp"/> should produce.</summary>
    protected int expectedResult;

    /// <summary>A reconnecting client.</summary>
    protected ReconnectingClient client = new(TimeProvider.System);

    /// <summary>A step of fake time.</summary>
    protected TimeSpan step = TimeSpan.FromSeconds(30);

    /// <summary>An action under test.</summary>
    protected Func<Task> action = () => Task.CompletedTask;

    /// <summary>A queue under test.</summary>
    protected Queue<int> queue = new();

    /// <summary>A start-up task.</summary>
    protected Task startup = Task.CompletedTask;

    /// <summary>An HTTP client.</summary>
    protected HttpClient httpClient = new();
}
