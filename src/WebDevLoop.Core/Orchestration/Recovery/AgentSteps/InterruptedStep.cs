using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>A step recovery finished because no live session owned it; its owner's work may need relaunching.</summary>
public sealed record InterruptedStep(StepRunId StepRunId, StepKind Kind, RunId SpecRunId, TicketRunId? TicketRunId);

/// <param name="Interrupted">Every step finished by recovery.</param>
/// <param name="TicketsNeedingAttention">Tickets whose step was interrupted more often in a row than <c>MaxRetries</c> allows.</param>
/// <param name="SpecsNeedingAttention">Specs whose step was interrupted more often in a row than <c>MaxRetries</c> allows.</param>
public sealed record StepInterruptionResult(
    IReadOnlyList<InterruptedStep> Interrupted,
    IReadOnlyList<TicketRunId> TicketsNeedingAttention,
    IReadOnlyList<RunId> SpecsNeedingAttention,
    int ConcurrencyConflicts);

public sealed record RelaunchResult(IReadOnlyList<RecoveredWork> Relaunched, IReadOnlyList<RunId> SpecsAwaitingPreparation);
