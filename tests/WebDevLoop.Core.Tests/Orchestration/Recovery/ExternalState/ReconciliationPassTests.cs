using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

public sealed class ReconciliationPassTests
{
    private readonly ExternalStateFixture _x = new();

    private IntegrationFixture F => _x.Integration;

    [Fact]
    public async Task Awaiting_merge_spec_whose_stack_merged_on_github_is_completed()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        await F.IntegrateAsync(ticket);
        foreach (SpecRunStatus next in new[] { SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge })
        {
            spec.TransitionTo(next, IntegrationFixture.T0);
        }

        F.Pulls.MergeStatus = StackMergeStatus.Merged;

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(SpecRunStatus.Completed, spec.Status);
        Assert.Equal(MergeTrackingOutcome.Completed, report.MergeTracking.Merges[spec.Id].Outcome);
    }

    [Fact]
    public async Task A_failing_step_of_one_spec_is_reported_while_the_other_specs_are_reconciled()
    {
        SpecRun broken = F.SeedRunningSpec();
        SpecRun healthy = _x.RunningSpec();
        F.Issues.Seed(new IssueRef(IntegrationFixture.RepoRef.Owner, IntegrationFixture.RepoRef.Name, 7), "Ticket 7", healthy.ParentIssue);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        ReconciliationFault fault = Assert.Single(report.Faults);
        Assert.Equal((F.Repository.Id, broken.Id, nameof(TicketGraphReconciler)), (fault.RepositoryId, fault.SpecRunId, fault.Step));
        Assert.Single(_x.Tickets(healthy));
    }

    [Fact]
    public async Task A_failing_fetch_is_reported_for_the_repository_and_its_specs_are_skipped()
    {
        SpecRun spec = _x.RunningSpec();
        F.Issues.Seed(new IssueRef(IntegrationFixture.RepoRef.Owner, IntegrationFixture.RepoRef.Name, 7), "Ticket 7", spec.ParentIssue);
        _x.DecorateGit = git => new FailingFetchGitWorkspace(git);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        ReconciliationFault fault = Assert.Single(report.Faults);
        Assert.Equal((F.Repository.Id, (RunId?)null), (fault.RepositoryId, fault.SpecRunId));
        Assert.Contains("unreachable", fault.Error, StringComparison.Ordinal);
        Assert.Empty(_x.Tickets(spec));
    }

    [Fact]
    public async Task Queued_and_preparing_specs_are_left_to_the_queue_and_preparation()
    {
        SpecRun spec = _x.RunningSpec();
        SpecRun queued = SpecRun.Queue(F.Ids.NewRunId(), F.Repository.Id, spec.ParentIssue, "Queued", "body", 999, IntegrationFixture.T0);
        F.Store.Add(queued);
        F.Issues.Seed(new IssueRef(IntegrationFixture.RepoRef.Owner, IntegrationFixture.RepoRef.Name, 7), "Ticket 7", spec.ParentIssue);

        await _x.ReconcileAsync();

        Assert.Single(_x.Tickets(spec));
        Assert.Empty(_x.Tickets(queued));
    }

    private sealed class FailingFetchGitWorkspace(IGitWorkspace inner) : IGitWorkspace
    {
        public Task EnsureClonedAsync(GitRepositoryLocation repo, CancellationToken cancellationToken) => inner.EnsureClonedAsync(repo, cancellationToken);

        public Task FetchAsync(GitRepositoryLocation repo, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The remote is unreachable.");

        public Task<CommitSha?> GetBranchTipAsync(GitRepositoryLocation repo, BranchName branch, GitRefScope scope, CancellationToken cancellationToken) =>
            inner.GetBranchTipAsync(repo, branch, scope, cancellationToken);

        public Task<bool> IsAncestorAsync(GitRepositoryLocation repo, CommitSha ancestor, CommitSha descendant, CancellationToken cancellationToken) =>
            inner.IsAncestorAsync(repo, ancestor, descendant, cancellationToken);

        public Task<CommitSha?> MergeBaseAsync(GitRepositoryLocation repo, CommitSha first, CommitSha second, CancellationToken cancellationToken) =>
            inner.MergeBaseAsync(repo, first, second, cancellationToken);

        public Task<RefUpdateResult> UpdateBranchAsync(GitRepositoryLocation repo, BranchName branch, CommitSha newTip, CommitSha? expectedPriorTip, CancellationToken cancellationToken) =>
            inner.UpdateBranchAsync(repo, branch, newTip, expectedPriorTip, cancellationToken);

        public Task<TicketWorktree> PrepareWorktreeAsync(GitRepositoryLocation repo, WorktreeSpec spec, CancellationToken cancellationToken) =>
            inner.PrepareWorktreeAsync(repo, spec, cancellationToken);

        public Task<WorktreeInspection> InspectWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
            inner.InspectWorktreeAsync(repo, worktreePath, cancellationToken);

        public Task<GitMergeResult> MergeIntoWorktreeAsync(TicketWorktree worktree, CommitSha source, string message, CancellationToken cancellationToken) =>
            inner.MergeIntoWorktreeAsync(worktree, source, message, cancellationToken);

        public Task<GitMergeResult> CreateSquashCommitAsync(GitRepositoryLocation repo, SquashRequest request, CancellationToken cancellationToken) =>
            inner.CreateSquashCommitAsync(repo, request, cancellationToken);

        public Task<IReadOnlyList<string>> GetChangedFilesAsync(GitRepositoryLocation repo, CommitSha from, CommitSha to, CancellationToken cancellationToken) =>
            inner.GetChangedFilesAsync(repo, from, to, cancellationToken);

        public Task<PushOutcome> PushAsync(GitRepositoryLocation repo, RefPush push, CancellationToken cancellationToken) => inner.PushAsync(repo, push, cancellationToken);

        public Task<WorktreeChanges> GetWorktreeChangesAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
            inner.GetWorktreeChangesAsync(repo, worktreePath, cancellationToken);

        public Task<WorktreeCleanResult> CleanWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
            inner.CleanWorktreeAsync(repo, worktreePath, cancellationToken);

        public Task<WorktreeCleanupResult> CleanupWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
            inner.CleanupWorktreeAsync(repo, worktreePath, cancellationToken);
    }
}
