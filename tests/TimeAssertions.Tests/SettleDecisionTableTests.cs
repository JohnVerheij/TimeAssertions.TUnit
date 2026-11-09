using System;
using System.Threading;
using System.Threading.Tasks;
using TimeAssertions;

namespace TimeAssertions.Tests;

/// <summary>
/// The settle of <see cref="ObservableFakeTimeProvider.AdvanceAndSettleAsync"/> as a decision table: a one-shot
/// timer fires on the advance, its callback does one thing, and the row says whether the settle returns (the woken
/// loop armed its next wait) or keeps waiting until its token is canceled. Only arming counts: creating a timer with a
/// due time, or a <c>Change</c> that arms a timer that was not pending (a fired one-shot, or one created or set
/// disabled). Moving a timer that is still pending, such as extending a watchdog, is not an arm.
/// </summary>
[Timeout(10_000)]
internal sealed class SettleDecisionTableTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    /// <summary>What the fired timer's callback does.</summary>
    internal enum Callback
    {
        /// <summary>Creates the next one-shot timer with a due time.</summary>
        CreatesTheNextTimer,

        /// <summary>Re-arms its own fired one-shot timer with <c>Change</c>.</summary>
        ReArmsItselfWithChange,

        /// <summary>Creates the next timer disabled (<see cref="Timeout.InfiniteTimeSpan"/>).</summary>
        CreatesTheNextTimerDisabled,

        /// <summary>Disposes its own timer and arms nothing.</summary>
        DisposesItself,

        /// <summary>Disposes its own timer and creates the next one.</summary>
        DisposesItselfAndCreatesTheNext,

        /// <summary>Extends another timer that is still pending (a watchdog's <c>CancelAfter</c>).</summary>
        ExtendsAPendingTimer,

        /// <summary>Arms another timer that was created disabled.</summary>
        ArmsADisabledTimer,

        /// <summary>Keeps its own timer disabled with <c>Change(Infinite, Infinite)</c>.</summary>
        KeepsItselfDisabled,

        /// <summary>Re-arms its own timer with <c>Change</c> after an asynchronous step.</summary>
        ReArmsItselfAfterAYield,
    }

    /// <summary>One row of the table: the callback, and whether the settle returns.</summary>
    [Test]
    [Arguments(Callback.CreatesTheNextTimer, true)]
    [Arguments(Callback.ReArmsItselfWithChange, true)]
    [Arguments(Callback.CreatesTheNextTimerDisabled, false)]
    [Arguments(Callback.DisposesItself, false)]
    [Arguments(Callback.DisposesItselfAndCreatesTheNext, true)]
    [Arguments(Callback.ExtendsAPendingTimer, false)]
    [Arguments(Callback.ArmsADisabledTimer, true)]
    [Arguments(Callback.KeepsItselfDisabled, false)]
    [Arguments(Callback.ReArmsItselfAfterAYield, true)]
    public async Task OneShotFires_TheCallbackDecidesWhetherTheSettleReturns(Callback callback, bool settles, CancellationToken cancellationToken)
    {
        var time = new ObservableFakeTimeProvider(Start);
        var timers = new Timers(
            time.CreateTimer(static _ => { }, state: null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan),
            time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));
        timers.Fired = time.CreateTimer(_ => timers.AsyncStep = Act(callback, time, timers), state: null, OneSecond, Timeout.InfiniteTimeSpan);
        var cancelTheWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var settle = time.AdvanceAndSettleAsync(OneSecond, cancelTheWait.Token);
            await timers.AsyncStep;
            if (settles)
            {
                await settle;
            }
            else
            {
                await Assert.That(settle.IsCompleted).IsFalse();
                await cancelTheWait.CancelAsync();
                _ = await Task.WhenAny(settle);
                await Assert.That(settle.IsCanceled).IsTrue();
            }
        }
        finally
        {
            cancelTheWait.Dispose();
        }
    }

    /// <summary>Runs the row's callback; returns the asynchronous step it started, or a completed task.</summary>
    private static Task Act(Callback callback, ObservableFakeTimeProvider time, Timers timers)
    {
        switch (callback)
        {
            case Callback.CreatesTheNextTimer:
                _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
                break;
            case Callback.ReArmsItselfWithChange:
                _ = timers.Fired!.Change(OneSecond, Timeout.InfiniteTimeSpan);
                break;
            case Callback.CreatesTheNextTimerDisabled:
                _ = time.CreateTimer(static _ => { }, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                break;
            case Callback.DisposesItself:
                timers.Fired!.Dispose();
                break;
            case Callback.DisposesItselfAndCreatesTheNext:
                timers.Fired!.Dispose();
                _ = time.CreateTimer(static _ => { }, state: null, OneSecond, Timeout.InfiniteTimeSpan);
                break;
            case Callback.ExtendsAPendingTimer:
                _ = timers.Pending.Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
                break;
            case Callback.ArmsADisabledTimer:
                _ = timers.Disabled.Change(OneSecond, Timeout.InfiniteTimeSpan);
                break;
            case Callback.KeepsItselfDisabled:
                _ = timers.Fired!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                break;
            case Callback.ReArmsItselfAfterAYield:
                return ReArmAfterAYieldAsync(timers.Fired!);
            default:
                throw new ArgumentOutOfRangeException(nameof(callback), callback, message: null);
        }

        return Task.CompletedTask;
    }

    private static async Task ReArmAfterAYieldAsync(ITimer timer)
    {
        await Task.Yield();
        _ = timer.Change(OneSecond, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The timers of one row: a pending one, a disabled one, and the one that fires.</summary>
    private sealed class Timers(ITimer pending, ITimer disabled)
    {
        /// <summary>Gets a timer that is pending (due in a minute) during the settle.</summary>
        public ITimer Pending { get; } = pending;

        /// <summary>Gets a timer created disabled.</summary>
        public ITimer Disabled { get; } = disabled;

        /// <summary>Gets or sets the one-shot timer that fires on the advance.</summary>
        public ITimer? Fired { get; set; }

        /// <summary>Gets or sets the asynchronous step the callback started.</summary>
        public Task AsyncStep { get; set; } = Task.CompletedTask;
    }
}
