using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

/// <summary>Running specs follow ticket graph changes made on GitHub after the snapshot.</summary>
public sealed class TicketGraphReconciliationTests
{
    private readonly ExternalStateFixture _x = new();

    private IntegrationFixture F => _x.Integration;

    [Fact]
    public async Task New_open_sub_issue_gets_a_blocked_ticket_run_with_its_blocking_edges_and_requests_a_frontier_reconciliation()
    {
        SpecRun spec = await SnapshotSpecAsync((1, []), (2, [1]));
        IssueRef added = SeedSubIssue(spec, 3, 2);
        IssueRef closed = SeedSubIssue(spec, 4);
        await F.Issues.CloseAsync(closed, IssueCloseReason.Completed, ExternalStateFixture.Token);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        TicketRun ticket = Assert.Single(_x.Tickets(spec), candidate => candidate.Issue.Number == added.Number);
        Assert.Equal(TicketRunStatus.Blocked, ticket.Status);
        Assert.DoesNotContain(_x.Tickets(spec), candidate => candidate.Issue.Number == closed.Number);
        TicketDependency edge = Assert.Single(_x.Dependencies(spec), dependency => dependency.BlockedTicketRunId == ticket.Id);
        Assert.Equal(Ticket(spec, 2).Id, edge.BlockingTicketRunId);
        Assert.Equal(DependencySource.GitHub, edge.Source);
        Assert.Single(_x.Pending<FrontierReconciliationRequested>(), requested => requested.SpecRunId == spec.Id);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.TicketAdded } && action.TicketRunId == ticket.Id);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.TicketDependencyAdded } && action.TicketRunId == ticket.Id);

        ExternalReconciliationReport again = await _x.ReconcileAsync();

        Assert.Empty(again.Actions);
        Assert.Equal(3, _x.Tickets(spec).Count);
        Assert.Single(_x.Pending<FrontierReconciliationRequested>());
    }

    [Fact]
    public async Task Unstarted_ticket_whose_issue_was_closed_outside_the_app_is_skipped_and_no_longer_blocks_its_dependents()
    {
        SpecRun spec = await SnapshotSpecAsync((1, []), (2, [1]));
        TicketRun closed = Ticket(spec, 1);
        await F.Issues.CloseAsync(closed.Issue, IssueCloseReason.Completed, ExternalStateFixture.Token);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.Skipped, closed.Status);
        Assert.Empty(_x.Dependencies(spec));
        Assert.Single(_x.Pending<TicketRunStatusChanged>(), changed => changed.TicketRunId == closed.Id && changed.To == TicketRunStatus.Skipped);
        Assert.Single(_x.RunEvents(spec), runEvent => runEvent.Type == ExternalStateRunEvents.TicketClosedExternally && runEvent.TicketRunId == closed.Id);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.TicketSkipped } && action.TicketRunId == closed.Id);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.TicketDependencyRemoved } && action.TicketRunId == Ticket(spec, 2).Id);
    }

    [Fact]
    public async Task Unstarted_ticket_removed_from_the_spec_is_skipped_and_its_edges_are_dropped()
    {
        SpecRun spec = await SnapshotSpecAsync((1, []), (2, [1]));
        TicketRun removed = Ticket(spec, 1);
        F.Issues.RemoveSubIssue(spec.ParentIssue, removed.Issue);

        await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.Skipped, removed.Status);
        Assert.Empty(_x.Dependencies(spec));
        Assert.Single(_x.RunEvents(spec), runEvent => runEvent.Type == ExternalStateRunEvents.TicketRemovedFromSpec && runEvent.TicketRunId == removed.Id);
        Assert.Equal(TicketRunStatus.Blocked, Ticket(spec, 2).Status);
    }

    [Fact]
    public async Task Changed_blocking_relations_are_mirrored_for_unstarted_tickets_only()
    {
        SpecRun spec = await SnapshotSpecAsync((1, []), (2, [1]), (3, [1]));
        TicketRun started = Ticket(spec, 2);
        started.TransitionTo(TicketRunStatus.Ready, IntegrationFixture.T0);
        started.TransitionTo(TicketRunStatus.Implementing, IntegrationFixture.T0);
        F.Issues.SetBlockedBy(started.Issue);
        F.Issues.SetBlockedBy(Ticket(spec, 3).Issue, started.Issue);

        await _x.ReconcileAsync();

        Assert.Equal(
            [(2, 1), (3, 2)],
            _x.Dependencies(spec)
                .Select(edge => (Number(spec, edge.BlockedTicketRunId), Number(spec, edge.BlockingTicketRunId)))
                .Order()
                .ToArray());
    }

    [Fact]
    public async Task Reopened_issue_of_a_ticket_skipped_as_closed_gets_a_new_ticket_run()
    {
        SpecRun spec = await SnapshotSpecAsync((1, []));
        TicketRun skipped = Ticket(spec, 1);
        await F.Issues.CloseAsync(skipped.Issue, IssueCloseReason.Completed, ExternalStateFixture.Token);
        await _x.ReconcileAsync();
        F.Issues.Reopen(skipped.Issue);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.Skipped, skipped.Status);
        TicketRun reopened = Assert.Single(_x.Tickets(spec), ticket => ticket.Issue.Number == 1 && ticket.Id != skipped.Id);
        Assert.Equal(TicketRunStatus.Blocked, reopened.Status);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.TicketAdded } && action.TicketRunId == reopened.Id);
    }

    [Fact]
    public async Task Reopened_issue_of_an_integrated_ticket_is_recorded_once_and_the_ticket_stays_integrated()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun integrated = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await F.IntegrateAsync(integrated);
        F.Issues.Reopen(integrated.Issue);

        ExternalReconciliationReport report = await _x.ReconcileAsync();
        ExternalReconciliationReport again = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.Integrated, integrated.Status);
        Assert.Single(_x.Tickets(spec));
        Assert.Single(_x.RunEvents(spec), runEvent => runEvent.Type == ExternalStateRunEvents.IntegratedTicketReopened && runEvent.TicketRunId == integrated.Id);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.IntegratedTicketReopened } && action.TicketRunId == integrated.Id);
        Assert.Empty(again.Actions);
    }

    [Fact]
    public async Task Specs_that_are_not_running_keep_their_ticket_graph()
    {
        SpecRun spec = await SnapshotSpecAsync((1, []));
        spec.TransitionTo(SpecRunStatus.ParentReviewing, IntegrationFixture.T0);
        SeedSubIssue(spec, 2);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Single(_x.Tickets(spec));
        Assert.Empty(report.Actions);
    }

    /// <summary>A running spec whose sub-issues (number, blocked-by numbers) were snapshotted as ticket runs.</summary>
    private async Task<SpecRun> SnapshotSpecAsync(params (int Number, int[] BlockedBy)[] tickets)
    {
        SpecRun spec = _x.RunningSpec();
        foreach ((int number, int[] blockedBy) in tickets)
        {
            SeedSubIssue(spec, number, blockedBy);
        }

        Assert.Null(await new SpecSnapshotter(F.Issues, F.Store, F.Ids, F.Clock).SnapshotAsync(spec, ExternalStateFixture.Token));
        return spec;
    }

    private IssueRef SeedSubIssue(SpecRun spec, int number, params int[] blockedBy)
    {
        IssueRef issue = Issue(number);
        F.Issues.Seed(issue, $"Ticket {number}", spec.ParentIssue, blockedBy.Select(Issue).ToArray());
        return issue;
    }

    private TicketRun Ticket(SpecRun spec, int number) => _x.Tickets(spec).Last(ticket => ticket.Issue.Number == number);

    private int Number(SpecRun spec, TicketRunId id) => _x.Tickets(spec).Single(ticket => ticket.Id == id).Issue.Number;

    private static IssueRef Issue(int number) => new(IntegrationFixture.RepoRef.Owner, IntegrationFixture.RepoRef.Name, number);
}
