using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Core.Tests.Queries;

public sealed class LiveEventViewMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly RunId Run = new("run-1");

    [Fact]
    public void spec_status_change_exposes_run_and_new_status()
    {
        var envelope = new EventEnvelope(5, new SpecRunStatusChanged(Run, 3, SpecRunStatus.Queued, SpecRunStatus.Preparing, At));

        Assert.Equal(new LiveEventView(5, nameof(SpecRunStatusChanged), "run-1", null, null, nameof(SpecRunStatus.Preparing), At), envelope.ToView());
    }

    [Fact]
    public void ticket_status_change_exposes_ticket_and_new_status()
    {
        var envelope = new EventEnvelope(6, new TicketRunStatusChanged(Run, new TicketRunId("t-1"), TicketRunStatus.Ready, TicketRunStatus.Implementing, At));

        Assert.Equal(new LiveEventView(6, nameof(TicketRunStatusChanged), "run-1", "t-1", null, nameof(TicketRunStatus.Implementing), At), envelope.ToView());
    }

    [Fact]
    public void saga_checkpoint_advance_exposes_run_ticket_and_checkpoint()
    {
        var envelope = new EventEnvelope(8, new SagaCheckpointAdvanced(Run, new TicketRunId("t-1"), IntegrationSagaCheckpoint.PrCreated, At));

        Assert.Equal(new LiveEventView(8, nameof(SagaCheckpointAdvanced), "run-1", "t-1", null, nameof(IntegrationSagaCheckpoint.PrCreated), At), envelope.ToView());
    }

    [Fact]
    public void every_workflow_event_naming_a_spec_run_is_mapped_so_pages_can_filter_by_run()
    {
        Type[] eventTypes = typeof(WorkflowEvent).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(WorkflowEvent)) && !type.IsAbstract && type.GetProperty("SpecRunId") is not null)
            .ToArray();
        Assert.NotEmpty(eventTypes);

        foreach (Type type in eventTypes)
        {
            var workflowEvent = (WorkflowEvent)Sample(type);

            Assert.True(new EventEnvelope(1, workflowEvent).ToView().SpecRunId == Run.Value, $"{type.Name} is not mapped by LiveEventViewMapper.");
        }
    }

    private static object Sample(Type type)
    {
        System.Reflection.ConstructorInfo constructor = type.GetConstructors().Single();
        object?[] arguments = constructor.GetParameters().Select(parameter => SampleValue(parameter.ParameterType)).ToArray();
        return constructor.Invoke(arguments);
    }

    private static object? SampleValue(Type type) => type switch
    {
        _ when type == typeof(RunId) => Run,
        _ when type == typeof(TicketRunId) => new TicketRunId("t-1"),
        _ when type == typeof(StepRunId) => new StepRunId("s-1"),
        _ when type == typeof(DateTimeOffset) => At,
        _ when type.IsEnum => Enum.GetValues(type).GetValue(0),
        _ when Nullable.GetUnderlyingType(type) is not null => null,
        _ => type.IsValueType ? Activator.CreateInstance(type) : null,
    };

    [Fact]
    public void step_status_change_exposes_step_and_optional_ticket()
    {
        var withTicket = new EventEnvelope(7, new StepRunStatusChanged(Run, new TicketRunId("t-1"), new StepRunId("s-1"), StepStatus.Running, At));
        var parentStep = new EventEnvelope(8, new StepRunStatusChanged(Run, null, new StepRunId("s-2"), StepStatus.Succeeded, At));

        Assert.Equal(new LiveEventView(7, nameof(StepRunStatusChanged), "run-1", "t-1", "s-1", nameof(StepStatus.Running), At), withTicket.ToView());
        Assert.Equal(new LiveEventView(8, nameof(StepRunStatusChanged), "run-1", null, "s-2", nameof(StepStatus.Succeeded), At), parentStep.ToView());
    }

    [Fact]
    public void other_run_events_expose_only_the_run()
    {
        Assert.Equal(new LiveEventView(9, nameof(SpecRunQueued), "run-1", null, null, null, At), new EventEnvelope(9, new SpecRunQueued(Run, 3, At)).ToView());
        Assert.Equal(new LiveEventView(10, nameof(FrontierReconciliationRequested), "run-1", null, null, null, At), new EventEnvelope(10, new FrontierReconciliationRequested(Run, At)).ToView());
    }

    [Fact]
    public void an_event_of_an_unknown_kind_is_still_projected_by_type_name()
    {
        var envelope = new EventEnvelope(11, new UnknownEvent(At));

        Assert.Equal(new LiveEventView(11, nameof(UnknownEvent), null, null, null, null, At), envelope.ToView());
    }

    private sealed record UnknownEvent(DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
}
