namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Shared selection-handling rules for enqueue outcomes.</summary>
public static class EnqueueOutcomeRules
{
    /// <summary>A new or existing unfinished run handles the selection; failures remain selected for retry.</summary>
    public static bool IsHandled(this EnqueueOutcome outcome) =>
        outcome is EnqueueOutcome.Queued or EnqueueOutcome.AlreadyQueued;
}
