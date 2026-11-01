using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// Shared wait primitives for the "eventually" active-timer assertions. The wait is event-driven:
/// <see cref="ObservableTimeProvider.WaitUntilAsync"/> re-checks the condition each time a timer is created,
/// re-scheduled, fired, or disposed, so nothing polls. An optional timeout bounds the wait in real time.
/// </summary>
internal static class TimerWait
{
    /// <summary>The longest timeout a <see cref="CancellationTokenSource"/> accepts: <c>uint.MaxValue - 1</c>
    /// milliseconds, about 49.7 days.</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1.0);

    /// <summary>Validates a timeout and returns it unchanged. Validating when the assertion is built makes an
    /// out-of-range timeout fail there, not later during the wait.</summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <returns><paramref name="timeout"/> when valid, as the optional bound the assertions store.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative or longer than
    /// <see cref="MaxTimeout"/>.</exception>
    public static TimeSpan? ValidateTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, MaxTimeout);
        return timeout;
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds for <paramref name="provider"/>. Without a
    /// <paramref name="timeout"/>, only <paramref name="cancellationToken"/> bounds the wait.
    /// </summary>
    /// <param name="provider">The provider whose changes wake the wait.</param>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="timeout">An optional bound in real time; <see langword="null"/> waits for the token only.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns><see langword="true"/> when the condition held; <see langword="false"/> when
    /// <paramref name="timeout"/> passed first.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled first.</exception>
    public static async Task<bool> UntilAsync(
        ObservableTimeProvider provider,
        Func<ObservableTimeProvider, bool> condition,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is not { } bound)
        {
            await provider.WaitUntilAsync(condition, cancellationToken).ConfigureAwait(false);
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (condition(provider))
        {
            return true;
        }

        if (bound == TimeSpan.Zero)
        {
            return false;
        }

        // The bound runs on the real clock on purpose: it limits how long a test waits for a change that
        // never comes. The provider's own clock is usually a fake one that only moves when the test advances it.
        using var timeoutSource = new CancellationTokenSource(bound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await provider.WaitUntilAsync(condition, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout passed first; one last check covers a change that raced it.
            return condition(provider);
        }
    }

    /// <summary>
    /// Builds the assertion failure that surfaces a source exception (an exception thrown while the
    /// assertion source was being evaluated) rather than masking it behind a timeout message.
    /// </summary>
    /// <param name="exception">The exception captured by TUnit during source evaluation.</param>
    /// <returns>A failed <see cref="AssertionResult"/> carrying the exception.</returns>
    public static AssertionResult FailFromSourceException(Exception exception)
    {
        return AssertionResult.Failed(
            $"threw {exception.GetType().Name}: {exception.Message}",
            exception);
    }
}
