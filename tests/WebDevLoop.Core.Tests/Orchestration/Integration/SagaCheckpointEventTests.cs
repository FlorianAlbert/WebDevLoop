using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class SagaCheckpointEventTests
{
    private readonly IntegrationFixture _f = new();

    [Fact]
    public async Task Every_checkpoint_of_a_saga_is_announced_through_the_outbox_in_order()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");

        await _f.IntegrateAsync(ticket);

        SagaCheckpointAdvanced[] events = _f.Store.PendingEvents.OfType<SagaCheckpointAdvanced>().ToArray();
        Assert.Equal(Enum.GetValues<IntegrationSagaCheckpoint>(), events.Select(announced => announced.Checkpoint));
        Assert.All(events, announced => Assert.Equal((spec.Id, ticket.Id), (announced.SpecRunId, announced.TicketRunId)));
    }

    [Fact]
    public async Task A_resumed_saga_announces_only_the_checkpoints_it_still_passes()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");
        _f.JournaledPulls.FailNextCreate = new HttpRequestException("503 Service Unavailable");
        await _f.IntegrateAsync(ticket);
        int announcedBefore = _f.Store.PendingEvents.OfType<SagaCheckpointAdvanced>().Count();

        await _f.IntegrateAsync(ticket);

        IntegrationSagaCheckpoint[] resumed = _f.Store.PendingEvents.OfType<SagaCheckpointAdvanced>().Skip(announcedBefore).Select(announced => announced.Checkpoint).ToArray();
        Assert.Equal(
            Enum.GetValues<IntegrationSagaCheckpoint>().Where(checkpoint => checkpoint >= IntegrationSagaCheckpoint.PrCreated),
            resumed);
    }
}
