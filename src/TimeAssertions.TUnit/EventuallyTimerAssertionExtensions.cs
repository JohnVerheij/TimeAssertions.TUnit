using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using TimeAssertions;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// The pre-0.10.0 <c>pollingInterval</c> forms of the "eventually" active-timer assertions, kept for source
/// compatibility. Since 0.10.0 the waits are event-driven (they react to every timer change), so a polling
/// interval has no effect; each overload forwards to the form without it.
/// </summary>
/// <remarks>
/// These overloads are hand-written rather than generated: the TUnit source generator does not carry an
/// <see cref="ObsoleteAttribute"/> from an assertion constructor onto the generated extension, so a deprecated
/// constructor would give callers no warning.
/// </remarks>
[SuppressMessage(
    "Info Code Smell",
    "S1133:Deprecated code should be removed",
    Justification = "Deliberate one-release deprecation window: these overloads are removed in 0.11.0, as their Obsolete message states.")]
public static class EventuallyTimerAssertionExtensions
{
    private const string PollingIntervalObsolete =
        "The wait is event-driven since 0.10.0, so pollingInterval has no effect; remove the argument. This overload will be removed in 0.11.0.";

    /// <summary>Waits until no timer remains active, or <paramref name="timeout"/> passes.</summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative.</param>
    /// <param name="pollingInterval">Ignored; the wait is event-driven.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's <paramref name="timeout"/> expression.</param>
    /// <returns>The <see cref="HasNoActiveTimersEventuallyAssertion"/> chain.</returns>
    [Obsolete(PollingIntervalObsolete)]
    public static HasNoActiveTimersEventuallyAssertion HasNoActiveTimersEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        TimeSpan timeout,
        TimeSpan? pollingInterval,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        _ = pollingInterval;
        return source.HasNoActiveTimersEventually(timeout, cancellationToken, timeoutExpression: timeoutExpression);
    }

    /// <summary>Waits until at least one timer is active, or <paramref name="timeout"/> passes.</summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative.</param>
    /// <param name="pollingInterval">Ignored; the wait is event-driven.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's <paramref name="timeout"/> expression.</param>
    /// <returns>The <see cref="HasActiveTimersEventuallyAssertion"/> chain.</returns>
    [Obsolete(PollingIntervalObsolete)]
    public static HasActiveTimersEventuallyAssertion HasActiveTimersEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        TimeSpan timeout,
        TimeSpan? pollingInterval,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        _ = pollingInterval;
        return source.HasActiveTimersEventually(timeout, cancellationToken, timeoutExpression: timeoutExpression);
    }

    /// <summary>Waits until the active-timer count equals <paramref name="count"/>, or <paramref name="timeout"/> passes.</summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="count">The active-timer count to wait for. Must be non-negative.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative.</param>
    /// <param name="pollingInterval">Ignored; the wait is event-driven.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <param name="countExpression">Captured automatically; the caller's <paramref name="count"/> expression.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's <paramref name="timeout"/> expression.</param>
    /// <returns>The <see cref="HasActiveTimerCountEventuallyAssertion"/> chain.</returns>
    [Obsolete(PollingIntervalObsolete)]
    public static HasActiveTimerCountEventuallyAssertion HasActiveTimerCountEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        int count,
        TimeSpan timeout,
        TimeSpan? pollingInterval,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(count))] string? countExpression = null,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        _ = pollingInterval;
        return source.HasActiveTimerCountEventually(
            count, timeout, cancellationToken, countExpression: countExpression, timeoutExpression: timeoutExpression);
    }

    /// <summary>Waits until the active-timer count reaches at least <paramref name="count"/>, or <paramref name="timeout"/> passes.</summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="count">The minimum active-timer count to wait for. Must be non-negative.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative.</param>
    /// <param name="pollingInterval">Ignored; the wait is event-driven.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <param name="countExpression">Captured automatically; the caller's <paramref name="count"/> expression.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's <paramref name="timeout"/> expression.</param>
    /// <returns>The <see cref="HasAtLeastActiveTimerCountEventuallyAssertion"/> chain.</returns>
    [Obsolete(PollingIntervalObsolete)]
    public static HasAtLeastActiveTimerCountEventuallyAssertion HasAtLeastActiveTimerCountEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        int count,
        TimeSpan timeout,
        TimeSpan? pollingInterval,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(count))] string? countExpression = null,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        _ = pollingInterval;
        return source.HasAtLeastActiveTimerCountEventually(
            count, timeout, cancellationToken, countExpression: countExpression, timeoutExpression: timeoutExpression);
    }

    /// <summary>Waits until the active-timer count falls to at most <paramref name="count"/>, or <paramref name="timeout"/> passes.</summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="count">The maximum active-timer count to wait for. Must be non-negative.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative.</param>
    /// <param name="pollingInterval">Ignored; the wait is event-driven.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <param name="countExpression">Captured automatically; the caller's <paramref name="count"/> expression.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's <paramref name="timeout"/> expression.</param>
    /// <returns>The <see cref="HasAtMostActiveTimerCountEventuallyAssertion"/> chain.</returns>
    [Obsolete(PollingIntervalObsolete)]
    public static HasAtMostActiveTimerCountEventuallyAssertion HasAtMostActiveTimerCountEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        int count,
        TimeSpan timeout,
        TimeSpan? pollingInterval,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(count))] string? countExpression = null,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        _ = pollingInterval;
        return source.HasAtMostActiveTimerCountEventually(
            count, timeout, cancellationToken, countExpression: countExpression, timeoutExpression: timeoutExpression);
    }
}
