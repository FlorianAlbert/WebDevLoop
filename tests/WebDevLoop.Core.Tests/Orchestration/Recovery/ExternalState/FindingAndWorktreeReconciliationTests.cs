using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

public sealed class FindingAndWorktreeReconciliationTests
{
    private readonly ExternalStateFixture _x = new();

    private IntegrationFixture F => _x.Integration;

    [Fact]
    public async Task Finding_issue_created_before_a_crash_is_recorded_from_its_fingerprint_with_exactly_one_ticket_run()
    {
        SpecRun spec = _x.RunningSpec();
        var fingerprint = new FindingFingerprint("spec:parent-review:missing validation");
        FindingIssuance issuance = FindingIssuance.Plan(spec.Id, new StepRunId("review-1"), FindingAxis.Specification, fingerprint, IntegrationFixture.T0);
        F.Store.Add(issuance);
        var finding = new IssueRef(IntegrationFixture.RepoRef.Owner, IntegrationFixture.RepoRef.Name, 40, databaseId: 4000);
        F.Issues.SeedFindingIssue(finding, "Missing validation", spec.ParentIssue, fingerprint);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(FindingIssuanceStatus.Created, issuance.Status);
        Assert.Equal((40, 4000L), (issuance.IssueNumber, issuance.IssueDatabaseId));
        Assert.Single(_x.Tickets(spec), ticket => ticket.Issue.Number == finding.Number);
        Assert.Empty(F.Journal.Calls);
        Assert.Contains(report.Actions, action => action.Kind == ReconciliationActionKind.FindingIssuanceRecovered);
    }

    [Fact]
    public async Task Finding_planned_but_never_created_on_github_stays_planned_for_the_issuer()
    {
        SpecRun spec = _x.RunningSpec();
        spec.TransitionTo(SpecRunStatus.ParentReviewing, IntegrationFixture.T0);
        FindingIssuance issuance = FindingIssuance.Plan(spec.Id, new StepRunId("review-1"), FindingAxis.CodingStandards, new FindingFingerprint("never created"), IntegrationFixture.T0);
        F.Store.Add(issuance);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(FindingIssuanceStatus.Planned, issuance.Status);
        Assert.Empty(_x.Tickets(spec));
        Assert.Empty(report.Actions);
    }

    [Fact]
    public async Task Missing_worktree_of_a_reviewed_ticket_is_recreated_from_its_branch()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        string path = ticket.WorktreePath!;
        F.Git.DeleteWorktree(path);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        WorktreeInspection restored = await F.Git.InspectWorktreeAsync(F.Location, path, ExternalStateFixture.Token);
        Assert.Equal((WorktreeStatus.Clean, ticket.BranchName, ticket.LastImplementedSha), (restored.Status, restored.Branch, restored.Head));
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.WorktreeRestored } && action.TicketRunId == ticket.Id);
    }

    [Fact]
    public async Task Present_worktrees_and_worktrees_in_use_by_an_active_step_are_left_alone()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun dirty = F.SeedReviewedTicket(spec, 1, "one.cs");
        F.Git.SetWorktreeStatus(dirty.WorktreePath!, WorktreeStatus.Dirty);
        TicketRun busy = F.SeedReviewedTicket(spec, 2, "two.cs");
        F.Git.DeleteWorktree(busy.WorktreePath!);
        StepRun review = StepRun.Create(new StepRunId("review-2"), spec.Id, busy.Id, StepKind.Review, AgentRole.ReviewerSpecification, 1, "hash");
        review.Start(IntegrationFixture.T0, TimeSpan.FromMinutes(30));
        F.Store.Add(review);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Empty(F.CallsOf("prepare-worktree:"));
        Assert.Equal(WorktreeStatus.Dirty, (await F.Git.InspectWorktreeAsync(F.Location, dirty.WorktreePath!, ExternalStateFixture.Token)).Status);
        Assert.DoesNotContain(report.Actions, action => action.Kind == ReconciliationActionKind.WorktreeRestored);
    }

    [Fact]
    public async Task Local_integration_branch_lost_with_the_clone_is_restored_from_the_pushed_branch()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await F.IntegrateAsync(ticket);
        CommitSha pushed = F.Git.RemoteTip(spec.IntegrationBranch)!.Value;
        F.Git.DeleteLocalBranch(spec.IntegrationBranch);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(pushed, await F.Git.GetBranchTipAsync(F.Location, spec.IntegrationBranch, GitRefScope.Local, ExternalStateFixture.Token));
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.IntegrationBranchRestored } && action.SpecRunId == spec.Id);
    }

    [Fact]
    public async Task Local_integration_branch_behind_the_pushed_branch_is_fast_forwarded()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await F.IntegrateAsync(ticket);
        CommitSha pushed = F.Git.RemoteTip(spec.IntegrationBranch)!.Value;
        await F.Git.UpdateBranchAsync(F.Location, spec.IntegrationBranch, F.TrunkTip, pushed, ExternalStateFixture.Token);

        await _x.ReconcileAsync();

        Assert.Equal(pushed, F.LocalTip(spec.IntegrationBranch));
    }

    [Fact]
    public async Task Local_integration_branch_ahead_of_the_pushed_branch_is_kept()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await F.IntegrateAsync(ticket);
        CommitSha pushed = F.Git.RemoteTip(spec.IntegrationBranch)!.Value;
        CommitSha local = F.Git.Commit([pushed], "unpushed.cs");
        await F.Git.UpdateBranchAsync(F.Location, spec.IntegrationBranch, local, pushed, ExternalStateFixture.Token);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(local, F.LocalTip(spec.IntegrationBranch));
        Assert.DoesNotContain(report.Actions, action => action.Kind == ReconciliationActionKind.IntegrationBranchRestored);
    }
}
