using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace TimeAssertions.TUnit;

/// <summary>
/// TUnit assertion that waits until enough active timers on an <see cref="ObservableTimeProvider"/> carry a due
/// time: by default one, or at least <c>atLeast</c>. Generates the <c>HasTimerScheduledEventually</c> chain
/// extensions via TUnit's <see cref="AssertionExtensionAttribute"/> source generator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which timers count.</b> A timer counts while it carries the due time exactly, as the code under test
/// passed it, so the assertion tells several loops apart: "both ping loops armed their 30 s delay" while a
/// start-up gate and an hourly refresh are pending too. See <see cref="ActiveTimerAssertions.HasTimerScheduled"/>.
/// </para>
/// <para>
/// <b>Event-driven.</b> The condition is checked once, then again each time a timer is created,
/// re-scheduled, fired, or disposed through the provider (<see cref="ObservableTimeProvider.WaitUntilAsync"/>),
/// never on a polling loop.
/// </para>
/// <para>
/// <b>Bounds.</b> The <c>timeout</c> forms fail when <c>timeout</c> of real time passes first; the timeout is
/// a bound, not a poll interval. The token-only forms are bounded by the token only, typically the test's own.
/// When the token is canceled the wait throws <see cref="OperationCanceledException"/>.
/// </para>
/// <code>
/// _ = service.StartAsync(ct);
/// await Assert.That(time).HasTimerScheduledEventually(TimeSpan.FromSeconds(30), atLeast: 2, ct);
/// </code>
/// </remarks>
[AssertionExtension("HasTimerScheduledEventually")]
public sealed class HasTimerScheduledEventuallyAssertion : Assertion<ObservableTimeProvider>
{
    private readonly TimeSpan _dueTime;
    private readonly int _atLeast;
    private readonly TimeSpan? _timeout;
    private readonly CancellationToken _cancellationToken;

    /// <summary>The bound for messages: " within &lt;timeout&gt;", or empty for the token-only form.</summary>
    private readonly string _within;

    /// <summary>Initializes the assertion for one timer, with a real-time bound and a cancellation token.
    /// Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="dueTime">The due time to wait for, compared exactly. Must be non-negative.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative and at most about 49.7 days (<c>uint.MaxValue - 1</c> milliseconds).</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dueTime"/> or <paramref name="timeout"/>
    /// is negative, or <paramref name="timeout"/> is longer than about 49.7 days.</exception>
    public HasTimerScheduledEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan dueTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        : this(context, dueTime, 1, TimerWait.ValidateTimeout(timeout), cancellationToken)
    {
    }

    /// <summary>Initializes the assertion for one timer, bounded by <paramref name="cancellationToken"/> only.
    /// Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="dueTime">The due time to wait for, compared exactly. Must be non-negative.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dueTime"/> is negative.</exception>
    public HasTimerScheduledEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan dueTime,
        CancellationToken cancellationToken)
        : this(context, dueTime, 1, default(TimeSpan?), cancellationToken)
    {
    }

    /// <summary>Initializes the assertion for at least <paramref name="atLeast"/> timers, with a real-time bound
    /// and a cancellation token. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="dueTime">The due time to wait for, compared exactly. Must be non-negative.</param>
    /// <param name="atLeast">The minimum number of active timers carrying <paramref name="dueTime"/>. Must be
    /// positive.</param>
    /// <param name="timeout">The longest real time to wait. Must be non-negative and at most about 49.7 days (<c>uint.MaxValue - 1</c> milliseconds).</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dueTime"/> or <paramref name="timeout"/>
    /// is negative, <paramref name="timeout"/> is longer than about 49.7 days, or <paramref name="atLeast"/> is not
    /// positive.</exception>
    public HasTimerScheduledEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan dueTime,
        int atLeast,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        : this(context, dueTime, atLeast, TimerWait.ValidateTimeout(timeout), cancellationToken)
    {
    }

    /// <summary>Initializes the assertion for at least <paramref name="atLeast"/> timers, bounded by
    /// <paramref name="cancellationToken"/> only. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="dueTime">The due time to wait for, compared exactly. Must be non-negative.</param>
    /// <param name="atLeast">The minimum number of active timers carrying <paramref name="dueTime"/>. Must be
    /// positive.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dueTime"/> is negative, or
    /// <paramref name="atLeast"/> is not positive.</exception>
    public HasTimerScheduledEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan dueTime,
        int atLeast,
        CancellationToken cancellationToken)
        : this(context, dueTime, atLeast, default(TimeSpan?), cancellationToken)
    {
    }

    private HasTimerScheduledEventuallyAssertion(
        AssertionContext<ObservableTimeProvider> context,
        TimeSpan dueTime,
        int atLeast,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        : base(context)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(atLeast);
        _dueTime = dueTime;
        _atLeast = atLeast;
        _timeout = timeout;
        _within = timeout is { } bound ? " within " + TimeRenderingHelpers.FormatDuration(bound) : string.Empty;
        _cancellationToken = cancellationToken;
        Context.ExpressionBuilder.Append(
            CultureInfo.InvariantCulture,
            $".HasTimerScheduledEventually({TimeRenderingHelpers.FormatDuration(_dueTime)}{(_atLeast > 1 ? ", atLeast: " + _atLeast.ToString(CultureInfo.InvariantCulture) : string.Empty)}, {(_timeout is { } timeoutValue ? TimeRenderingHelpers.FormatDuration(timeoutValue) : "cancellationToken")})");
    }

    /// <inheritdoc/>
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<ObservableTimeProvider> metadata)
    {
        if (metadata.Exception is not null)
        {
            return TimerWait.FailFromSourceException(metadata.Exception);
        }

        var value = metadata.Value;
        if (value is null)
        {
            return AssertionResult.Failed("the observable time provider was null");
        }

        var reached = await TimerWait
            .UntilAsync(
                value,
                provider => ActiveTimerAssertions.CountScheduledAt(provider.ActiveTimers, _dueTime) >= _atLeast,
                _timeout,
                _cancellationToken)
            .ConfigureAwait(false);

        if (reached)
        {
            return AssertionResult.Passed;
        }

        var active = value.ActiveTimers;
        var actual = ActiveTimerAssertions.CountScheduledAt(active, _dueTime);
        return AssertionResult.Failed(
            string.Create(
                CultureInfo.InvariantCulture,
                $"expected at least {_atLeast} active timer(s) scheduled at {TimeRenderingHelpers.FormatDuration(_dueTime)}{_within} but found {actual} (minimum={_atLeast}, actual={actual}):")
            + TimeRenderingHelpers.FormatActiveTimerSurvivors(active));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"at least {_atLeast} active timer(s) scheduled at {TimeRenderingHelpers.FormatDuration(_dueTime)}{_within}");
    }
}
