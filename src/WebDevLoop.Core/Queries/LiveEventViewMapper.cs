using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Queries;

public static class LiveEventViewMapper
{
    private const string PassedStatus = "Passed";
    private const string RemovedStatus = "Removed";
    private const string RetainedStatus = "Retained";
    private const string AwaitingTrunkStatus = "AwaitingTrunk";

    public static LiveEventView ToView(this EventEnvelope envelope)
    {
        string type = envelope.Event.GetType().Name;
        DateTimeOffset at = envelope.Event.OccurredAt;
        return envelope.Event switch
        {
            SpecRunStatusChanged e => new(envelope.MessageId, type, e.SpecRunId.Value, null, null, e.To.ToString(), at),
            TicketRunStatusChanged e => new(envelope.MessageId, type, e.SpecRunId.Value, e.TicketRunId.Value, null, e.To.ToString(), at),
            StepRunStatusChanged e => new(envelope.MessageId, type, e.SpecRunId.Value, e.TicketRunId?.Value, e.StepRunId.Value, e.Status.ToString(), at),
            SagaCheckpointAdvanced e => new(envelope.MessageId, type, e.SpecRunId.Value, e.TicketRunId.Value, null, e.Checkpoint.ToString(), at),
            SpecRunQueued e => new(envelope.MessageId, type, e.SpecRunId.Value, null, null, null, at),
            FrontierReconciliationRequested e => new(envelope.MessageId, type, e.SpecRunId.Value, null, null, null, at),
            SpecTestingPassed e => new(envelope.MessageId, type, e.SpecRunId.Value, null, null, PassedStatus, at),
            SpecCompletionReported e => new(envelope.MessageId, type, e.SpecRunId.Value, null, null, e.IntegrationBranch.Value, at),
            SpecWorktreesCleanedUp e => new(envelope.MessageId, type, e.SpecRunId.Value, null, null, e.Warnings is { Count: > 0 } ? RetainedStatus : RemovedStatus, at),
            SpecStackAwaitingTrunk e => new(envelope.MessageId, type, e.SpecRunId.Value, null, null, AwaitingTrunkStatus, at),
            _ => new(envelope.MessageId, type, null, null, null, null, at),
        };
    }
}
