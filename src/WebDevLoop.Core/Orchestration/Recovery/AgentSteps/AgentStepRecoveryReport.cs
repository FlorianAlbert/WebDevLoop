using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>What one recovery pass did.</summary>
/// <param name="EvictedRuntimes">Copilot runtimes stopped because no session used them for the idle timeout.</param>
/// <param name="RefreshedRuntimes">Copilot runtimes replaced because their token was about to expire (the stale keys).</param>
/// <param name="RuntimeMaintenanceFailure">Why evicting or refreshing the runtimes failed; resumes then go through the runner's auth retry.</param>
/// <param name="InterruptedSteps">Steps no live session owned any more, finished by recovery.</param>
/// <param name="SpecsAwaitingPreparation">Preparing specs whose exploration was interrupted; preparation must run again.</param>
/// <param name="ConcurrencyConflicts">Items skipped because another writer changed them first; the next pass re-evaluates them.</param>
public sealed record AgentStepRecoveryReport(
    IReadOnlyList<CopilotRuntimeKey> EvictedRuntimes,
    IReadOnlyList<CopilotRuntimeKey> RefreshedRuntimes,
    string? RuntimeMaintenanceFailure,
    IReadOnlyList<StoppedTestLease> StoppedTestLeases,
    IReadOnlyList<StepRunId> InterruptedSteps,
    IReadOnlyList<TicketRunId> TicketsNeedingAttention,
    IReadOnlyList<RunId> SpecsNeedingAttention,
    IReadOnlyList<RecoveredWork> Relaunched,
    IReadOnlyList<RunId> SpecsAwaitingPreparation,
    int ConcurrencyConflicts);
