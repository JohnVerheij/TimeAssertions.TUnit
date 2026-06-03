using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// Shared polling primitives for the real-time "eventually" active-timer assertions
/// (<c>HasNoActiveTimersEventually</c> / <c>HasActiveTimerCountEventually</c>). Centralizes the
/// default poll interval, argument validation, and the deadline poll loop so the two assertion
/// classes stay consistent.
/// </summary>
/// <remarks>
/// The poll uses a real wall-clock <see cref="Task.Delay(TimeSpan, CancellationToken)"/> loop, not a
/// fake-time advance: the disposal of a hosted-service timer happens on a real asynchronous
/// continuation (after <c>StopAsync</c> returns), which a fake-clock tick cannot drive.
/// </remarks>
internal static class PollingDefaults
{
    /// <summary>The default delay between polls when the caller does not supply one.</summary>
    public static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>Validates a polling timeout and returns it unchanged.</summary>
    /// <param name="timeout">The maximum wall-clock time to poll for.</param>
    /// <returns><paramref name="timeout"/> when valid.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative.</exception>
    public static TimeSpan ValidateTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return timeout;
    }

    /// <summary>
    /// Resolves the effective poll interval, falling back to the default when the caller passes
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="pollingInterval">The caller-supplied interval, or <see langword="null"/> for the
    /// default.</param>
    /// <returns>The interval to use between polls.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pollingInterval"/> is supplied and
    /// is not strictly positive.</exception>
    public static TimeSpan ResolvePollingInterval(TimeSpan? pollingInterval)
    {
        if (pollingInterval is not { } interval)
        {
            return PollingInterval;
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingInterval),
                interval,
                "Poll interval must be strictly positive.");
        }

        return interval;
    }

    /// <summary>
    /// Polls <paramref name="condition"/> in real time until it returns <see langword="true"/> or the
    /// <paramref name="timeout"/> deadline elapses. The condition is evaluated once before the first
    /// delay, so an already-satisfied condition returns without waiting.
    /// </summary>
    /// <param name="condition">The predicate to poll. Re-evaluated after each
    /// <paramref name="pollingInterval"/> delay.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for.</param>
    /// <param name="pollingInterval">The delay between polls.</param>
    /// <param name="ct">A token that cancels the poll loop.</param>
    /// <returns><see langword="true"/> when <paramref name="condition"/> became (or already was)
    /// <see langword="true"/> within the deadline; otherwise <see langword="false"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was canceled (surfaced as a
    /// <see cref="TaskCanceledException"/> from the underlying delay).</exception>
    [SuppressMessage(
        "Design",
        "MA0167:Use an overload with a TimeProvider",
        Justification = "The poll must run on the real wall clock: hosted-service timer disposal happens on a real async continuation, which a fake TimeProvider's clock cannot advance. A TimeProvider-bound delay would never elapse under fake time.")]
    public static async Task<bool> PollUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        TimeSpan pollingInterval,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (condition())
        {
            return true;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var remaining = timeout - stopwatch.Elapsed;
            var delay = remaining < pollingInterval ? remaining : pollingInterval;

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            else
            {
                ct.ThrowIfCancellationRequested();
            }

            if (condition())
            {
                return true;
            }
        }

        return false;
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
