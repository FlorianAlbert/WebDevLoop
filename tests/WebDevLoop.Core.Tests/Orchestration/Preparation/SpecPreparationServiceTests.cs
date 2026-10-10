using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Tests.Orchestration.Preparation;

public sealed class SpecPreparationServiceTests
{
    private readonly SpecWorkflowFixture _fixture = new(explorationEnabled: false);

    private ITicketRunRepository TicketRuns => _fixture.Store;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task preparation_snapshots_the_ticket_dag_creates_the_integration_branch_from_trunk_and_starts_running()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1);
        _fixture.SeedTicket(3, spec: 1, blockedByTickets: 2);
        SpecRun run = await ActivateAsync(1);

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.Prepared, outcome);
        Assert.Equal(SpecRunStatus.Running, run.Status);
        Assert.True(_fixture.Git.IsCloned);
        Assert.Equal(_fixture.TrunkTip, run.IntegrationBaseSha);
        Assert.Equal(_fixture.TrunkTip, run.IntegrationTipSha);
        Assert.Equal(_fixture.TrunkTip, await LocalTipAsync(run.IntegrationBranch));

        IReadOnlyList<TicketRun> tickets = await TicketRuns.ListBySpecRunAsync(run.Id, Ct);
        Assert.Equal([2, 3], tickets.Select(ticket => ticket.Issue.Number).Order());
        Assert.All(tickets, ticket => Assert.Equal(TicketRunStatus.Blocked, ticket.Status));
        Assert.Equal("Ticket 3", tickets.Single(ticket => ticket.Issue.Number == 3).Title);
        TicketDependency edge = Assert.Single(await TicketRuns.ListDependenciesAsync(run.Id, Ct));
        Assert.Equal(TicketOf(tickets, 3), edge.BlockedTicketRunId);
        Assert.Equal(TicketOf(tickets, 2), edge.BlockingTicketRunId);
        Assert.Equal(DependencySource.GitHub, edge.Source);

        Assert.Contains(_fixture.Store.PendingEvents, e => e is SpecRunStatusChanged { From: SpecRunStatus.Preparing, To: SpecRunStatus.Running });
        Assert.Contains(_fixture.Store.PendingEvents, e => e is FrontierReconciliationRequested requested && requested.SpecRunId == run.Id);
    }

    [Fact]
    public async Task closed_sub_issues_and_blockers_outside_the_spec_are_not_snapshotted()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1);
        _fixture.SeedTicket(3, spec: 1, blockedByTickets: 2);
        _fixture.Issues.Seed(SpecWorkflowFixture.Issue(99), "Elsewhere");
        _fixture.SeedTicket(4, spec: 1, blockedByTickets: 99);
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(2), IssueCloseReason.Completed, Ct);
        SpecRun run = await ActivateAsync(1);

        await _fixture.PrepareAsync(run);

        IReadOnlyList<TicketRun> tickets = await TicketRuns.ListBySpecRunAsync(run.Id, Ct);
        Assert.Equal([3, 4], tickets.Select(ticket => ticket.Issue.Number).Order());
        Assert.Empty(await TicketRuns.ListDependenciesAsync(run.Id, Ct));
    }

    [Fact]
    public async Task ticket_dependency_cycle_needs_attention_before_any_branch_is_created()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1, blockedByTickets: 3);
        _fixture.SeedTicket(3, spec: 1, blockedByTickets: 2);
        SpecRun run = await ActivateAsync(1);

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.NeedsAttention, outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Equal(AttentionCode.TicketDependencyCycle, run.Attention!.Code);
        Assert.Contains("cycle", run.FailureReason);
        Assert.Empty(await TicketRuns.ListBySpecRunAsync(run.Id, Ct));
        Assert.Null(await LocalTipAsync(run.IntegrationBranch));
        Assert.Contains(_fixture.Store.PendingEvents, e => e is SpecRunStatusChanged { To: SpecRunStatus.NeedsAttention });
    }

    [Fact]
    public async Task spec_without_open_ticket_sub_issues_needs_attention_instead_of_reaching_review()
    {
        _fixture.SeedSpec(1);
        SpecRun run = await ActivateAsync(1);

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.NeedsAttention, outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Equal(AttentionCode.SpecHasNoTickets, run.Attention!.Code);
        Assert.Contains("no open ticket sub-issues", run.FailureReason);
        Assert.Empty(await TicketRuns.ListBySpecRunAsync(run.Id, Ct));
        Assert.Null(await LocalTipAsync(run.IntegrationBranch));
        Assert.Empty(_fixture.Agents.Started);
        Assert.DoesNotContain(_fixture.Store.PendingEvents, e => e is SpecRunStatusChanged { To: SpecRunStatus.Running });
    }

    [Fact]
    public async Task spec_whose_sub_issues_are_all_closed_needs_attention()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1);
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(2), IssueCloseReason.Completed, Ct);
        SpecRun run = await ActivateAsync(1);

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.NeedsAttention, outcome);
        Assert.Contains("no open ticket sub-issues", run.FailureReason);
    }

    [Fact]
    public async Task missing_trunk_on_the_remote_needs_attention()
    {
        _fixture.GlobalSettings.BaseBranch = new BranchName("develop");
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1);
        SpecRun run = await ActivateAsync(1);

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.NeedsAttention, outcome);
        Assert.Contains("develop", run.FailureReason);
        Assert.Equal(AttentionCode.BaseBranchMissing, run.Attention!.Code);
        Assert.Contains("'develop'", run.Attention.Summary, StringComparison.Ordinal);
        Assert.Contains(run.Attention.UserSteps, step => step.LinkHref is "/settings#section-general");
    }

    [Fact]
    public async Task an_integration_branch_left_by_an_earlier_attempt_needs_attention_with_the_automatic_reset_on_offer()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1);
        SpecRun run = await ActivateAsync(1);
        CommitSha leftover = _fixture.Git.Commit([], "leftover.txt");
        await _fixture.Git.UpdateBranchAsync(GitRepositoryLocation.From(_fixture.Repository), run.IntegrationBranch, leftover, null, Ct);

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.NeedsAttention, outcome);
        Assert.Equal(AttentionCode.IntegrationBranchExists, run.Attention!.Code);
        Assert.True(run.Attention.AutoFixPending);
        Assert.Contains(leftover.Value, run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task run_that_is_not_preparing_is_left_untouched()
    {
        _fixture.SeedSpec(1);
        SpecRun queued = await _fixture.EnqueueAsync(1);

        PreparationOutcome outcome = await _fixture.PrepareAsync(queued);

        Assert.Equal(PreparationOutcome.NotPreparing, outcome);
        Assert.Equal(SpecRunStatus.Queued, queued.Status);
        Assert.False(_fixture.Git.IsCloned);
    }

    [Fact]
    public async Task preparing_again_after_a_retry_reuses_the_snapshot_and_integration_branch()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1);
        SpecRun run = await ActivateAsync(1);
        await _fixture.PrepareAsync(run);
        run.MarkNeedsAttention(AttentionReasons.Unclassified("parked for the test", true), _fixture.Clock.UtcNow);
        run.TransitionTo(SpecRunStatus.Preparing, _fixture.Clock.UtcNow);
        _fixture.Git.SeedRemoteBranch(SpecWorkflowFixture.Trunk, "moved.txt");

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.Prepared, outcome);
        Assert.Single(await TicketRuns.ListBySpecRunAsync(run.Id, Ct));
        Assert.Equal(_fixture.TrunkTip, run.IntegrationBaseSha);
        Assert.Equal(_fixture.TrunkTip, await LocalTipAsync(run.IntegrationBranch));
    }

    [Fact]
    public async Task losing_the_preparation_save_race_reports_a_conflict_before_touching_branches()
    {
        _fixture.SeedSpec(1);
        SpecRun run = await ActivateAsync(1);
        _fixture.Store.ConflictOnNextSave = true;

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.ConcurrencyConflict, outcome);
        Assert.Null(await LocalTipAsync(run.IntegrationBranch));
    }

    private static TicketRunId TicketOf(IEnumerable<TicketRun> tickets, int issueNumber) =>
        tickets.Single(ticket => ticket.Issue.Number == issueNumber).Id;

    private async Task<SpecRun> ActivateAsync(int specNumber)
    {
        SpecRun run = await _fixture.EnqueueAsync(specNumber);
        await _fixture.ScheduleAsync();
        Assert.Equal(SpecRunStatus.Preparing, run.Status);
        return run;
    }

    private Task<CommitSha?> LocalTipAsync(BranchName branch) =>
        _fixture.Git.GetBranchTipAsync(GitRepositoryLocation.From(_fixture.Repository), branch, GitRefScope.Local, Ct);
}
