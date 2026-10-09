using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class IntegrationEventHandlerTests
{
    private readonly IntegrationFixture _f = new();
    private readonly RecordingIntegrationLauncher _launcher = new();

    [Fact]
    public async Task Ticket_entering_integrating_is_launched_with_its_repository()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");

        await HandleAsync(new TicketRunStatusChanged(spec.Id, ticket.Id, TicketRunStatus.Reviewing, TicketRunStatus.Integrating, IntegrationFixture.T0));

        Assert.Equal([new IntegrationAssignment(_f.Repository.Id, spec.Id, ticket.Id)], _launcher.Launched);
    }

    [Fact]
    public async Task Integrated_ticket_relaunches_the_specs_other_integrating_tickets_that_may_wait_for_its_layer()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun integrated = _f.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun waiting = _f.SeedReviewedTicket(spec, 2, "second.cs");
        _f.SeedReviewedTicket(spec, 3, "third.cs");
        IntegrationFixture.MoveToIntegrating(integrated);
        IntegrationFixture.MoveToIntegrating(waiting);
        integrated.TransitionTo(TicketRunStatus.Integrated, IntegrationFixture.T0);

        await HandleAsync(new TicketRunStatusChanged(spec.Id, integrated.Id, TicketRunStatus.Integrating, TicketRunStatus.Integrated, IntegrationFixture.T0));

        Assert.Equal([new IntegrationAssignment(_f.Repository.Id, spec.Id, waiting.Id)], _launcher.Launched);
    }

    [Theory]
    [InlineData(TicketRunStatus.Implementing, TicketRunStatus.Reviewing)]
    [InlineData(TicketRunStatus.Integrating, TicketRunStatus.NeedsAttention)]
    public async Task Other_transitions_launch_nothing(TicketRunStatus from, TicketRunStatus to)
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");

        await HandleAsync(new TicketRunStatusChanged(spec.Id, ticket.Id, from, to, IntegrationFixture.T0));

        Assert.Empty(_launcher.Launched);
    }

    private Task HandleAsync(WorkflowEvent workflowEvent) =>
        new IntegrationEventHandler(_launcher, _f.Store, _f.Store).HandleAsync(new EventEnvelope(1, workflowEvent), IntegrationFixture.Token);

    private sealed class RecordingIntegrationLauncher : IIntegrationLauncher
    {
        public List<IntegrationAssignment> Launched { get; } = [];

        public void Launch(IntegrationAssignment assignment) => Launched.Add(assignment);
    }
}
