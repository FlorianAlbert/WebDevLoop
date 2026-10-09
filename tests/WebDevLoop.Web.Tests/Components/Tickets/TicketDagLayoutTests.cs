using WebDevLoop.Core.Domain;
using WebDevLoop.Web.Components.Tickets;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Tickets;

public sealed class TicketDagLayoutTests
{
    [Fact]
    public void Tickets_are_layered_by_longest_blocker_chain()
    {
        var rows = TicketDagLayout.Build(
        [
            Views.Ticket("c", 3, TicketRunStatus.Blocked, blockedBy: ["a", "b"]),
            Views.Ticket("b", 2, TicketRunStatus.Blocked, blockedBy: ["a"]),
            Views.Ticket("a", 1, TicketRunStatus.Integrated),
        ]);

        Assert.Equal(["a", "b", "c"], rows.Select(row => row.Ticket.Id));
        Assert.Equal([0, 1, 2], rows.Select(row => row.Layer));
    }

    [Fact]
    public void Ticket_is_frontier_only_when_all_blockers_are_integrated()
    {
        var rows = TicketDagLayout.Build(
        [
            Views.Ticket("a", 1, TicketRunStatus.Integrated),
            Views.Ticket("b", 2, TicketRunStatus.Implementing),
            Views.Ticket("c", 3, TicketRunStatus.Blocked, blockedBy: ["a"]),
            Views.Ticket("d", 4, TicketRunStatus.Blocked, blockedBy: ["a", "b"]),
            Views.Ticket("e", 5, TicketRunStatus.Ready),
        ]);

        Assert.Equal(["e", "c"], rows.Where(row => row.IsFrontier).Select(row => row.Ticket.Id));
    }

    [Fact]
    public void Blockers_carry_issue_and_status_of_the_blocking_ticket()
    {
        var rows = TicketDagLayout.Build(
        [
            Views.Ticket("a", 7, TicketRunStatus.Reviewing),
            Views.Ticket("b", 8, TicketRunStatus.Blocked, blockedBy: ["a"]),
        ]);

        TicketBlocker blocker = Assert.Single(rows.Single(row => row.Ticket.Id == "b").Blockers);
        Assert.Equal(7, blocker.Issue);
        Assert.Equal(TicketRunStatus.Reviewing, blocker.Status);
        Assert.False(blocker.IsSatisfied);
    }

    [Fact]
    public void Unknown_blocker_never_satisfies_and_does_not_add_a_layer()
    {
        var rows = TicketDagLayout.Build([Views.Ticket("b", 2, TicketRunStatus.Blocked, blockedBy: ["missing"])]);

        TicketDagRow row = Assert.Single(rows);
        Assert.Equal(0, row.Layer);
        Assert.False(row.IsFrontier);
        Assert.Null(row.Blockers.Single().Status);
    }

    [Fact]
    public void Dependency_cycle_does_not_hang_layout()
    {
        var rows = TicketDagLayout.Build(
        [
            Views.Ticket("a", 1, TicketRunStatus.Blocked, blockedBy: ["b"]),
            Views.Ticket("b", 2, TicketRunStatus.Blocked, blockedBy: ["a"]),
        ]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.False(row.IsFrontier));
    }
}
