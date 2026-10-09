using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

public sealed class SagaRecoveryTests
{
    /// <summary>Saga start, then <c>SquashCommitCreated</c>, <c>IntegrationRefUpdated</c>, <c>IntegrationPushed</c>, <c>StackBranchPushed</c>.</summary>
    private const int StackBranchPushedSave = 5;

    private const int IntegrationPushedSave = 4;

    private readonly ExternalStateFixture _x = new();

    private IntegrationFixture F => _x.Integration;

    [Fact]
    public async Task Pull_request_missing_in_the_database_but_found_by_its_exact_head_ref_is_recorded_without_creating_another()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await _x.CrashIntegrationAfterCallAsync(ticket, "create-pr:");
        Assert.Equal(IntegrationSagaCheckpoint.StackBranchPushed, F.Saga(ticket)!.Checkpoint);
        Assert.Null(ticket.PullRequestNumber);
        PullRequestSnapshot existing = Assert.Single(F.Pulls.PullRequests);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Single(F.Pulls.PullRequests);
        Assert.Single(F.CallsOf("create-pr:"));
        Assert.Equal(existing.Number, ticket.PullRequestNumber);
        Assert.Equal(existing.Number, Assert.Single(F.Layers(spec)).PullRequestNumber);
        Assert.Equal(TicketRunStatus.Integrated, ticket.Status);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.IntegrationResumed } && action.TicketRunId == ticket.Id);
        Assert.Empty(report.Faults);
    }

    [Fact]
    public async Task Crash_after_the_stack_branch_push_resumes_at_pull_request_creation_only()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await _x.CrashIntegrationAfterSaveAsync(ticket, StackBranchPushedSave);
        IntegrationSaga saga = F.Saga(ticket)!;
        Assert.Equal(IntegrationSagaCheckpoint.StackBranchPushed, saga.Checkpoint);
        int before = F.Journal.Calls.Count;

        await _x.ReconcileAsync();

        Assert.Equal([$"create-pr:{saga.StackBranchName}", $"close:{ticket.Issue}"], _x.CallsSince(before));
        Assert.Equal(TicketRunStatus.Integrated, ticket.Status);
        Assert.True(saga.IsCompleted);
    }

    [Fact]
    public async Task Integrated_commit_discovered_in_git_marks_the_ticket_integrated_and_signals_the_frontier()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun first = F.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun dependent = SeedBlockedTicket(spec, 2, blockedBy: first);
        await _x.CrashIntegrationAfterCallAsync(first, $"update-ref:{spec.IntegrationBranch}");
        IntegrationSaga saga = F.Saga(first)!;
        Assert.Equal(IntegrationSagaCheckpoint.SquashCommitCreated, saga.Checkpoint);
        Assert.Equal(saga.SquashCommitSha, F.LocalTip(spec.IntegrationBranch));
        Assert.Equal(F.TrunkTip, spec.IntegrationTipSha);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.Integrated, first.Status);
        Assert.Equal(saga.SquashCommitSha, first.IntegratedCommitSha);
        Assert.Equal(saga.SquashCommitSha, spec.IntegrationTipSha);
        Assert.Single(F.CallsOf("squash"));
        Assert.Single(_x.Pending<TicketRunStatusChanged>(), changed => changed.TicketRunId == first.Id && changed.To == TicketRunStatus.Integrated);
        Assert.Equal(TicketRunStatus.Blocked, dependent.Status);
        Assert.Empty(report.Faults);
    }

    [Fact]
    public async Task Old_stack_branch_from_a_prior_run_of_the_same_spec_is_never_moved()
    {
        SpecRun old = _x.RunningSpec();
        TicketRun oldTicket = F.SeedReviewedTicket(old, 1, "old.cs");
        await F.IntegrateAsync(oldTicket);
        BranchName oldStack = F.Saga(oldTicket)!.StackBranchName;
        CommitSha oldStackTip = F.Git.RemoteTip(oldStack)!.Value;
        TicketRun oldSecond = F.SeedReviewedTicket(old, 2, "old2.cs");
        await _x.CrashIntegrationAfterSaveAsync(oldSecond, IntegrationPushedSave);
        IntegrationSaga oldSaga = F.Saga(oldSecond)!;
        old.TransitionTo(SpecRunStatus.Aborted, IntegrationFixture.T0);
        oldSecond.TransitionTo(TicketRunStatus.Aborted, IntegrationFixture.T0);
        await F.Issues.CloseAsync(oldSecond.Issue, IssueCloseReason.NotPlanned, ExternalStateFixture.Token);
        F.Issues.Reopen(oldTicket.Issue);
        SpecRun spec = _x.SeedRunningSpec(old.ParentIssue);
        TicketRun ticket = _x.SeedReviewedTicketForExistingIssue(spec, oldTicket.Issue, "new.cs");
        await _x.CrashIntegrationAfterSaveAsync(ticket, StackBranchPushedSave);
        int before = F.Journal.Calls.Count;

        await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.Integrated, ticket.Status);
        Assert.NotEqual(oldStack, F.Saga(ticket)!.StackBranchName);
        Assert.Equal(oldStackTip, F.Git.RemoteTip(oldStack));
        Assert.DoesNotContain(_x.CallsSince(before), call => call.Contains(old.Id.Value, StringComparison.Ordinal));
        Assert.Equal(IntegrationSagaCheckpoint.IntegrationPushed, oldSaga.Checkpoint);
        Assert.Null(F.Git.RemoteTip(oldSaga.StackBranchName));
    }

    [Fact]
    public async Task Pull_request_on_the_run_scoped_stack_branch_without_this_runs_identifiers_is_not_adopted()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await _x.CrashIntegrationAfterSaveAsync(ticket, StackBranchPushedSave);
        BranchName stack = F.Saga(ticket)!.StackBranchName;
        PullRequestSnapshot foreign = await F.Pulls.CreateDraftPullRequestAsync(
            IntegrationFixture.RepoRef,
            new DraftPullRequest(stack, IntegrationFixture.Trunk, "Hand-made PR", "Opened by hand.", new RunId("other-run"), new TicketRunId("other-ticket")),
            ExternalStateFixture.Token);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Contains($"#{foreign.Number}", ticket.FailureReason, StringComparison.Ordinal);
        Assert.Null(ticket.PullRequestNumber);
        Assert.Empty(F.Layers(spec));
        Assert.Empty(F.CallsOf("create-pr:"));
        Assert.Equal(IntegrationSagaCheckpoint.StackBranchPushed, F.Saga(ticket)!.Checkpoint);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.ForeignPullRequestRejected } && action.TicketRunId == ticket.Id);
    }

    [Fact]
    public async Task Integrating_ticket_whose_saga_has_not_squashed_yet_is_handed_to_the_background_launcher()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        IntegrationFixture.MoveToIntegrating(ticket);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(F.AssignmentFor(ticket), Assert.Single(_x.Launcher.Launched));
        Assert.Empty(F.CallsOf("squash"));
        Assert.Equal(TicketRunStatus.Integrating, ticket.Status);
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.IntegrationLaunched } && action.TicketRunId == ticket.Id);
    }

    private TicketRun SeedBlockedTicket(SpecRun spec, int issueNumber, TicketRun blockedBy)
    {
        var issue = new IssueRef(IntegrationFixture.RepoRef.Owner, IntegrationFixture.RepoRef.Name, issueNumber);
        F.Issues.Seed(issue, $"Ticket {issueNumber}", spec.ParentIssue, blockedBy.Issue);
        TicketRun ticket = TicketRun.Create(F.Ids.NewTicketRunId(), spec.Id, issue, $"Ticket {issueNumber}", "body", IntegrationFixture.T0);
        F.Store.Add(ticket);
        F.Store.AddDependency(TicketDependency.Create(spec.Id, ticket.Id, blockedBy.Id, DependencySource.GitHub));
        return ticket;
    }
}
