using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Infrastructure.Events;

namespace WebDevLoop.Web.Tests.Components.Support;

internal static class Events
{
    private static long _nextMessageId;

    public static InProcessRunEventBus NewBus() => new(NullLogger<InProcessRunEventBus>.Instance);

    public static Task PublishAsync(this IRunEventBus bus, WorkflowEvent workflowEvent) =>
        bus.PublishAsync(new EventEnvelope(Interlocked.Increment(ref _nextMessageId), workflowEvent), CancellationToken.None);

    public static TicketRunStatusChanged TicketStatus(string runId, string ticketId, TicketRunStatus from, TicketRunStatus to) =>
        new(new RunId(runId), new TicketRunId(ticketId), from, to, Views.Now);

    public static StepRunStatusChanged StepStatus(string runId, string? ticketId, string stepId, StepStatus status) =>
        new(new RunId(runId), ticketId is null ? null : new TicketRunId(ticketId), new StepRunId(stepId), status, Views.Now);

    public static SpecRunStatusChanged SpecStatus(string runId, SpecRunStatus to) =>
        new(new RunId(runId), 1, SpecRunStatus.Running, to, Views.Now);

    public static SagaCheckpointAdvanced SagaAdvanced(string runId, string ticketId, IntegrationSagaCheckpoint checkpoint) =>
        new(new RunId(runId), new TicketRunId(ticketId), checkpoint, Views.Now);

    /// <summary>Stands in for an event type the live mapper does not know yet (no spec run id); pages ignore it.</summary>
    public sealed record UnmappedEvent() : WorkflowEvent(Views.Now);
}
