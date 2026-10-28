using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Frontier;

namespace WebDevLoop.Core.Tests.Orchestration.Frontier;

public sealed class TicketFrontierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly RunId Run = new("run1");

    [Fact]
    public void currently_unblocked_tickets_are_dispatchable_in_issue_order()
    {
        TicketRun second = Ticket(12);
        TicketRun first = Ticket(11);

        FrontierSnapshot snapshot = TicketFrontier.Compute([second, first], new HashSet<TicketRunId> { first.Id, second.Id });

        Assert.Equal([first.Id, second.Id], snapshot.Unblocked);
        Assert.Equal([first.Id, second.Id], snapshot.Dispatchable);
    }

    [Fact]
    public void ready_tickets_require_current_authorization_too()
    {
        TicketRun ready = Ticket(1);
        ready.TransitionTo(TicketRunStatus.Ready, T0);

        FrontierSnapshot blocked = TicketFrontier.Compute([ready], new HashSet<TicketRunId>());
        FrontierSnapshot free = TicketFrontier.Compute([ready], new HashSet<TicketRunId> { ready.Id });

        Assert.Empty(blocked.Dispatchable);
        Assert.Empty(free.Unblocked);
        Assert.Equal([ready.Id], free.Dispatchable);
    }

    [Theory]
    [InlineData(TicketRunStatus.Implementing)]
    [InlineData(TicketRunStatus.NeedsAttention)]
    [InlineData(TicketRunStatus.Aborted)]
    [InlineData(TicketRunStatus.Skipped)]
    public void selected_or_finished_tickets_are_not_dispatched(TicketRunStatus status)
    {
        TicketRun ticket = Ticket(1);
        ticket.TransitionTo(TicketRunStatus.Ready, T0);
        ticket.TransitionTo(status, T0);

        FrontierSnapshot snapshot = TicketFrontier.Compute([ticket], new HashSet<TicketRunId> { ticket.Id });

        Assert.Empty(snapshot.Unblocked);
        Assert.Empty(snapshot.Dispatchable);
    }

    private static TicketRun Ticket(int number) =>
        TicketRun.Create(new TicketRunId($"t{number}"), Run, new IssueRef("octo", "app", number), $"Ticket {number}", "body", T0);
}
