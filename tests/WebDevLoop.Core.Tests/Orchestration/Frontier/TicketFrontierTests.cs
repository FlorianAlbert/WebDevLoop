using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Frontier;

namespace WebDevLoop.Core.Tests.Orchestration.Frontier;

public sealed class TicketFrontierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly RunId Run = new("run1");

    private static readonly TicketRunStatus[] ToIntegrated =
    [
        TicketRunStatus.Ready,
        TicketRunStatus.Implementing,
        TicketRunStatus.Reviewing,
        TicketRunStatus.Integrating,
        TicketRunStatus.Integrated,
    ];

    [Fact]
    public void tickets_without_blockers_are_unblocked_and_dispatchable_in_issue_order()
    {
        TicketRun second = Ticket(12);
        TicketRun first = Ticket(11);

        FrontierSnapshot snapshot = TicketFrontier.Compute([second, first], []);

        Assert.Equal([first.Id, second.Id], snapshot.Unblocked);
        Assert.Equal([first.Id, second.Id], snapshot.Dispatchable);
    }

    [Fact]
    public void diamond_dag_unblocks_both_middle_tickets_only_after_the_root_is_integrated()
    {
        TicketRun root = Ticket(1);
        TicketRun left = Ticket(2);
        TicketRun right = Ticket(3);
        TicketRun bottom = Ticket(4);
        TicketDependency[] edges = [Edge(left, root), Edge(right, root), Edge(bottom, left), Edge(bottom, right)];

        FrontierSnapshot initially = TicketFrontier.Compute([root, left, right, bottom], edges);
        Advance(root, ToIntegrated);
        FrontierSnapshot afterRoot = TicketFrontier.Compute([root, left, right, bottom], edges);

        Assert.Equal([root.Id], initially.Unblocked);
        Assert.Equal([left.Id, right.Id], afterRoot.Unblocked);
        Assert.Equal([left.Id, right.Id], afterRoot.Dispatchable);
    }

    [Fact]
    public void a_blocker_that_is_only_partially_done_keeps_the_dependent_blocked()
    {
        TicketRun integrated = Advance(Ticket(1), ToIntegrated);
        TicketRun reviewing = Advance(Ticket(2), TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing);
        TicketRun bottom = Ticket(3);

        FrontierSnapshot snapshot = TicketFrontier.Compute([integrated, reviewing, bottom], [Edge(bottom, integrated), Edge(bottom, reviewing)]);

        Assert.Empty(snapshot.Unblocked);
        Assert.Empty(snapshot.Dispatchable);
    }

    [Fact]
    public void ready_tickets_stay_dispatchable_and_in_flight_or_finished_tickets_are_not()
    {
        TicketRun ready = Advance(Ticket(1), TicketRunStatus.Ready);
        TicketRun implementing = Advance(Ticket(2), TicketRunStatus.Ready, TicketRunStatus.Implementing);
        TicketRun integrated = Advance(Ticket(3), ToIntegrated);
        TicketRun needsAttention = Advance(Ticket(4), TicketRunStatus.Ready);
        needsAttention.MarkNeedsAttention(AttentionReasons.Unclassified("stuck", true), T0);

        FrontierSnapshot snapshot = TicketFrontier.Compute([ready, implementing, integrated, needsAttention], []);

        Assert.Empty(snapshot.Unblocked);
        Assert.Equal([ready.Id], snapshot.Dispatchable);
    }

    [Fact]
    public void a_ready_ticket_that_gained_an_unintegrated_blocker_is_not_dispatched()
    {
        TicketRun ready = Advance(Ticket(1), TicketRunStatus.Ready);
        TicketRun finding = Ticket(2);

        FrontierSnapshot snapshot = TicketFrontier.Compute([ready, finding], [Edge(ready, finding)]);

        Assert.Equal([finding.Id], snapshot.Unblocked);
        Assert.Equal([finding.Id], snapshot.Dispatchable);
    }

    [Fact]
    public void a_skipped_blocker_unblocks_its_dependent_but_an_aborted_one_does_not()
    {
        TicketRun skipped = Advance(Ticket(1), TicketRunStatus.Skipped);
        TicketRun aborted = Advance(Ticket(2), TicketRunStatus.Aborted);
        TicketRun afterSkipped = Ticket(3);
        TicketRun afterAborted = Ticket(4);

        FrontierSnapshot snapshot = TicketFrontier.Compute(
            [skipped, aborted, afterSkipped, afterAborted], [Edge(afterSkipped, skipped), Edge(afterAborted, aborted)]);

        Assert.Equal([afterSkipped.Id], snapshot.Unblocked);
        Assert.Equal([afterSkipped.Id], snapshot.Dispatchable);
    }

    [Fact]
    public void a_blocker_missing_from_the_snapshot_is_never_satisfied()
    {
        TicketRun ticket = Ticket(1);
        TicketRun outside = Ticket(99);

        FrontierSnapshot snapshot = TicketFrontier.Compute([ticket], [Edge(ticket, outside)]);

        Assert.Empty(snapshot.Unblocked);
        Assert.Empty(snapshot.Dispatchable);
    }

    private static TicketRun Ticket(int number) =>
        TicketRun.Create(new TicketRunId($"t{number}"), Run, new IssueRef("octo", "app", number), $"Ticket {number}", "body", T0);

    private static TicketDependency Edge(TicketRun blocked, TicketRun blocking) =>
        TicketDependency.Create(Run, blocked.Id, blocking.Id, DependencySource.GitHub);

    private static TicketRun Advance(TicketRun ticket, params TicketRunStatus[] path)
    {
        foreach (TicketRunStatus status in path)
        {
            ticket.TransitionTo(status, T0);
        }

        return ticket;
    }
}
