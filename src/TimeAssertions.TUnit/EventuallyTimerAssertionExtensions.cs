using System;
using System.Runtime.CompilerServices;
using System.Threading;
using TimeAssertions;
using TimeAssertions.TUnit;
using TUnit.Assertions.Core;

// Placed in TUnit's globally-imported assertion-extensions namespace (the same namespace the
// [AssertionExtension] source generator emits the canonical extensions into) so the positional-token
// sugar is discoverable wherever the generated extensions are, without an extra `using
// TimeAssertions.TUnit;`.
namespace TUnit.Assertions.Extensions;

/// <summary>
/// Hand-written sugar overloads for the real-time "eventually" active-timer assertions that let a
/// caller pass a <see cref="CancellationToken"/> <em>positionally</em> while keeping the default poll
/// interval. They forward to the source-generated canonical extensions
/// (<c>HasNoActiveTimersEventually</c>, <c>HasActiveTimerCountEventually</c>,
/// <c>HasAtLeastActiveTimerCountEventually</c>, <c>HasAtMostActiveTimerCountEventually</c>,
/// <c>HasActiveTimersEventually</c>) with <c>pollingInterval: null</c>.
/// </summary>
/// <remarks>
/// <para>
/// The canonical extensions place the optional <c>TimeSpan? pollingInterval</c> parameter before the
/// <c>CancellationToken</c>, so the positional call <c>HasNoActiveTimersEventually(timeout, token)</c>
/// does not bind: a <see cref="CancellationToken"/> does not convert to <see cref="Nullable{TimeSpan}"/>,
/// forcing the verbose <c>cancellationToken: token</c> named form. These overloads restore the natural
/// <c>(timeout, token)</c> and <c>(count, timeout, token)</c> shapes, matching TUnit's own
/// <c>WaitsFor</c> / <c>Eventually</c> convention where the token follows the timeout positionally.
/// </para>
/// <para>
/// <b>Why the <see cref="CancellationToken"/> parameter has no default.</b> If it defaulted, the
/// two-argument call <c>HasNoActiveTimersEventually(timeout)</c> would match both this overload and
/// the canonical one, producing a CS0121 ambiguity. With no default, the bare <c>(timeout)</c> call
/// binds only to the canonical extension, while the three-argument positional <c>(timeout, token)</c>
/// call binds only to this overload (the token cannot bind to the canonical's <c>pollingInterval</c>).
/// </para>
/// </remarks>
public static class EventuallyTimerAssertionExtensions
{
    /// <summary>
    /// Polls the live active-timer count until it reaches zero or <paramref name="timeout"/> elapses,
    /// using the default poll interval and the supplied <paramref name="cancellationToken"/>. The
    /// positional-token sugar for <c>HasNoActiveTimersEventually(timeout, pollingInterval: null,
    /// cancellationToken: cancellationToken)</c>.
    /// </summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for the active-timer count to reach
    /// zero. Must be non-negative.</param>
    /// <param name="cancellationToken">A token that cancels the poll loop. This parameter has no
    /// default so the bare <c>HasNoActiveTimersEventually(timeout)</c> call stays unambiguous against
    /// the canonical overload.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's literal
    /// <paramref name="timeout"/> expression, forwarded so failure messages name it.</param>
    /// <returns>The canonical <see cref="HasNoActiveTimersEventuallyAssertion"/> chain.</returns>
    public static HasNoActiveTimersEventuallyAssertion HasNoActiveTimersEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        return source.HasNoActiveTimersEventually(
            timeout,
            pollingInterval: null,
            cancellationToken: cancellationToken,
            timeoutExpression: timeoutExpression);
    }

    /// <summary>
    /// Polls the live active-timer count until it equals <paramref name="count"/> or
    /// <paramref name="timeout"/> elapses, using the default poll interval and the supplied
    /// <paramref name="cancellationToken"/>. The positional-token sugar for
    /// <c>HasActiveTimerCountEventually(count, timeout, pollingInterval: null,
    /// cancellationToken: cancellationToken)</c>.
    /// </summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="count">The active-timer count to poll for. Must be non-negative.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for the active-timer count to reach
    /// <paramref name="count"/>. Must be non-negative.</param>
    /// <param name="cancellationToken">A token that cancels the poll loop. This parameter has no
    /// default so the bare <c>HasActiveTimerCountEventually(count, timeout)</c> call stays unambiguous
    /// against the canonical overload.</param>
    /// <param name="countExpression">Captured automatically; the caller's literal
    /// <paramref name="count"/> expression, forwarded so failure messages name it.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's literal
    /// <paramref name="timeout"/> expression, forwarded so failure messages name it.</param>
    /// <returns>The canonical <see cref="HasActiveTimerCountEventuallyAssertion"/> chain.</returns>
    public static HasActiveTimerCountEventuallyAssertion HasActiveTimerCountEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        [CallerArgumentExpression(nameof(count))] string? countExpression = null,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        return source.HasActiveTimerCountEventually(
            count,
            timeout,
            pollingInterval: null,
            cancellationToken: cancellationToken,
            countExpression: countExpression,
            timeoutExpression: timeoutExpression);
    }

    /// <summary>
    /// Polls the live active-timer count until it is at least <paramref name="count"/> or
    /// <paramref name="timeout"/> elapses, using the default poll interval and the supplied
    /// <paramref name="cancellationToken"/>. The positional-token sugar for
    /// <c>HasAtLeastActiveTimerCountEventually(count, timeout, pollingInterval: null,
    /// cancellationToken: cancellationToken)</c>.
    /// </summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="count">The minimum active-timer count to poll for. Must be non-negative.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for. Must be non-negative.</param>
    /// <param name="cancellationToken">A token that cancels the poll loop. This parameter has no
    /// default so the bare <c>HasAtLeastActiveTimerCountEventually(count, timeout)</c> call stays
    /// unambiguous against the canonical overload.</param>
    /// <param name="countExpression">Captured automatically; the caller's literal
    /// <paramref name="count"/> expression, forwarded so failure messages name it.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's literal
    /// <paramref name="timeout"/> expression, forwarded so failure messages name it.</param>
    /// <returns>The canonical <see cref="HasAtLeastActiveTimerCountEventuallyAssertion"/> chain.</returns>
    public static HasAtLeastActiveTimerCountEventuallyAssertion HasAtLeastActiveTimerCountEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        [CallerArgumentExpression(nameof(count))] string? countExpression = null,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        return source.HasAtLeastActiveTimerCountEventually(
            count,
            timeout,
            pollingInterval: null,
            cancellationToken: cancellationToken,
            countExpression: countExpression,
            timeoutExpression: timeoutExpression);
    }

    /// <summary>
    /// Polls the live active-timer count until it is at most <paramref name="count"/> or
    /// <paramref name="timeout"/> elapses, using the default poll interval and the supplied
    /// <paramref name="cancellationToken"/>. The positional-token sugar for
    /// <c>HasAtMostActiveTimerCountEventually(count, timeout, pollingInterval: null,
    /// cancellationToken: cancellationToken)</c>.
    /// </summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="count">The maximum active-timer count to poll for. Must be non-negative.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for. Must be non-negative.</param>
    /// <param name="cancellationToken">A token that cancels the poll loop. This parameter has no
    /// default so the bare <c>HasAtMostActiveTimerCountEventually(count, timeout)</c> call stays
    /// unambiguous against the canonical overload.</param>
    /// <param name="countExpression">Captured automatically; the caller's literal
    /// <paramref name="count"/> expression, forwarded so failure messages name it.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's literal
    /// <paramref name="timeout"/> expression, forwarded so failure messages name it.</param>
    /// <returns>The canonical <see cref="HasAtMostActiveTimerCountEventuallyAssertion"/> chain.</returns>
    public static HasAtMostActiveTimerCountEventuallyAssertion HasAtMostActiveTimerCountEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        [CallerArgumentExpression(nameof(count))] string? countExpression = null,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        return source.HasAtMostActiveTimerCountEventually(
            count,
            timeout,
            pollingInterval: null,
            cancellationToken: cancellationToken,
            countExpression: countExpression,
            timeoutExpression: timeoutExpression);
    }

    /// <summary>
    /// Polls the live active-timer count until at least one timer is active or <paramref name="timeout"/>
    /// elapses, using the default poll interval and the supplied <paramref name="cancellationToken"/>.
    /// The positional-token sugar for <c>HasActiveTimersEventually(timeout, pollingInterval: null,
    /// cancellationToken: cancellationToken)</c>.
    /// </summary>
    /// <param name="source">The assertion source over an <see cref="ObservableTimeProvider"/>.</param>
    /// <param name="timeout">The maximum wall-clock time to poll for. Must be non-negative.</param>
    /// <param name="cancellationToken">A token that cancels the poll loop. This parameter has no
    /// default so the bare <c>HasActiveTimersEventually(timeout)</c> call stays unambiguous against the
    /// canonical overload.</param>
    /// <param name="timeoutExpression">Captured automatically; the caller's literal
    /// <paramref name="timeout"/> expression, forwarded so failure messages name it.</param>
    /// <returns>The canonical <see cref="HasActiveTimersEventuallyAssertion"/> chain.</returns>
    public static HasActiveTimersEventuallyAssertion HasActiveTimersEventually(
        this IAssertionSource<ObservableTimeProvider> source,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null)
    {
        return source.HasActiveTimersEventually(
            timeout,
            pollingInterval: null,
            cancellationToken: cancellationToken,
            timeoutExpression: timeoutExpression);
    }
}
