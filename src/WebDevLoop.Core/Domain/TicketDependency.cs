namespace WebDevLoop.Core.Domain;

public sealed class TicketDependency
{
    private TicketDependency()
    {
    }

    public long Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public TicketRunId BlockedTicketRunId { get; private set; }

    public TicketRunId BlockingTicketRunId { get; private set; }

    public DependencySource Source { get; private set; }

    public static TicketDependency Create(RunId specRunId, TicketRunId blocked, TicketRunId blocking, DependencySource source)
    {
        if (blocked == blocking)
        {
            throw new DependencyCycleException($"Ticket run '{blocked}' cannot depend on itself.");
        }

        return new()
        {
            SpecRunId = specRunId,
            BlockedTicketRunId = blocked,
            BlockingTicketRunId = blocking,
            Source = source,
        };
    }

    public DependencyEdge<TicketRunId> ToEdge() => new(BlockedTicketRunId, BlockingTicketRunId);
}
