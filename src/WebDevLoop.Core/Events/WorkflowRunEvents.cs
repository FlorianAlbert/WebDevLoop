using System.Text.Json;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Events;

/// <summary>
/// Maps the progress events of the workflow to the run event audit log shown on the run page, so the timeline can be
/// followed while a run is active. Housekeeping events (frontier nudges, control audits recorded by their own journal) map to nothing.
/// </summary>
public static class WorkflowRunEvents
{
    public static RunEvent? TryCreate(WorkflowEvent workflowEvent) => workflowEvent switch
    {
        SpecRunStatusChanged e => Create(e.SpecRunId, null, e, new { from = e.From.ToString(), to = e.To.ToString() }),
        TicketRunStatusChanged e => Create(e.SpecRunId, e.TicketRunId, e, new { from = e.From.ToString(), to = e.To.ToString() }),
        StepRunStatusChanged e => Create(e.SpecRunId, e.TicketRunId, e, new { step = e.StepRunId.Value, to = e.Status.ToString() }),
        SagaCheckpointAdvanced e => Create(e.SpecRunId, e.TicketRunId, e, new { to = e.Checkpoint.ToString() }),
        _ => null,
    };

    private static RunEvent Create(RunId specRunId, TicketRunId? ticketRunId, WorkflowEvent source, object payload) =>
        RunEvent.Create(specRunId, ticketRunId, source.GetType().Name, JsonSerializer.Serialize(payload), source.OccurredAt);
}
