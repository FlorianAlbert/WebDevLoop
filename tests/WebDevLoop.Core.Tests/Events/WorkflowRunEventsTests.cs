using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Tests.Events;

public sealed class WorkflowRunEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly RunId Run = new("run-1");
    private static readonly TicketRunId Ticket = new("t-1");

    [Fact]
    public void Spec_status_change_is_recorded_on_the_run()
    {
        RunEvent runEvent = WorkflowRunEvents.TryCreate(new SpecRunStatusChanged(Run, 1, SpecRunStatus.Queued, SpecRunStatus.Preparing, Now))!;

        Assert.Equal(Run, runEvent.SpecRunId);
        Assert.Null(runEvent.TicketRunId);
        Assert.Equal(nameof(SpecRunStatusChanged), runEvent.Type);
        Assert.Contains("\"to\":\"Preparing\"", runEvent.PayloadJson);
        Assert.Equal(Now, runEvent.OccurredAt);
    }

    [Fact]
    public void Ticket_and_step_changes_are_recorded_against_the_ticket()
    {
        RunEvent ticket = WorkflowRunEvents.TryCreate(new TicketRunStatusChanged(Run, Ticket, TicketRunStatus.Ready, TicketRunStatus.Implementing, Now))!;
        RunEvent step = WorkflowRunEvents.TryCreate(new StepRunStatusChanged(Run, Ticket, new StepRunId("s-1"), StepStatus.Running, Now))!;

        Assert.Equal(Ticket, ticket.TicketRunId);
        Assert.Equal(Ticket, step.TicketRunId);
        Assert.Contains("\"to\":\"Implementing\"", ticket.PayloadJson);
        Assert.Contains("\"step\":\"s-1\"", step.PayloadJson);
        Assert.Contains("\"to\":\"Running\"", step.PayloadJson);
    }

    [Fact]
    public void Saga_checkpoints_are_recorded()
    {
        RunEvent runEvent = WorkflowRunEvents.TryCreate(new SagaCheckpointAdvanced(Run, Ticket, IntegrationSagaCheckpoint.Started, Now))!;

        Assert.Equal(nameof(SagaCheckpointAdvanced), runEvent.Type);
        Assert.Equal(Ticket, runEvent.TicketRunId);
    }

    [Fact]
    public void Housekeeping_events_are_not_recorded()
    {
        Assert.Null(WorkflowRunEvents.TryCreate(new FrontierReconciliationRequested(Run, Now)));
    }
}
