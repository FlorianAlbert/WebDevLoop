using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>
/// Agent-step recovery, the second stage of every <see cref="RecoveryCoordinator"/> cycle (startup, after
/// prerequisites and external reconciliation and before schedulers; and periodic). One pass:
/// <list type="number">
/// <item>stops Copilot runtimes no session used for their idle timeout (e.g. per-tester-port runtimes), then replaces
/// runtimes whose token is about to expire, so resumed sessions start on a fresh runtime;</item>
/// <item>kills leftover processes of tester leases no live tester owns and releases them;</item>
/// <item>finishes steps no live session owns (started by a previous process, testers whose lease was stopped, or steps
/// running well past their timeout), and moves owners interrupted more often in a row than <c>MaxRetries</c> allows to
/// <c>NeedsAttention</c>;</item>
/// <item>relaunches stalled work through the normal launchers; runners resume interrupted Copilot sessions by id and fall
/// back to a fresh session at the step's safe boundary when the session is gone.</item>
/// </list>
/// Work of this process that is still running within its timeout is never touched, so passes are safe to repeat.
/// </summary>
public sealed class AgentStepRecoveryService(
    ICopilotRuntimePool runtimes,
    OrphanedTestLeaseStopper leases,
    InterruptedStepFinisher steps,
    StalledWorkRelauncher relauncher) : IAgentStepRecovery
{
    public async Task<AgentStepRecoveryReport> RecoverAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<CopilotRuntimeKey> evicted, IReadOnlyList<CopilotRuntimeKey> refreshed, string? runtimeFailure) =
            await MaintainRuntimesAsync(cancellationToken);
        IReadOnlyList<StoppedTestLease> stoppedLeases = await leases.StopAsync(cancellationToken);
        StepInterruptionResult interruption = await steps.FinishAsync(stoppedLeases, cancellationToken);
        RelaunchResult relaunch = await relauncher.RelaunchAsync(interruption.Interrupted, cancellationToken);
        return new AgentStepRecoveryReport(
            evicted,
            refreshed,
            runtimeFailure,
            stoppedLeases,
            interruption.Interrupted.Select(step => step.StepRunId).ToArray(),
            interruption.TicketsNeedingAttention,
            interruption.SpecsNeedingAttention,
            relaunch.Relaunched,
            relaunch.SpecsAwaitingPreparation,
            interruption.ConcurrencyConflicts);
    }

    /// <summary>
    /// Idle runtimes are evicted first so they are not needlessly refreshed. A failure does not stop recovery: the runner's
    /// authentication retry replaces the runtime on demand.
    /// </summary>
    private async Task<(IReadOnlyList<CopilotRuntimeKey> Evicted, IReadOnlyList<CopilotRuntimeKey> Refreshed, string? Failure)> MaintainRuntimesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CopilotRuntimeKey> evicted = [];
        try
        {
            evicted = await runtimes.EvictIdleAsync(cancellationToken);
            return (evicted, await runtimes.RefreshExpiringAsync(cancellationToken), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (evicted, [], $"Maintaining the Copilot runtimes failed: {exception.Message}");
        }
    }
}
