using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Workflow steps 13–14 (entry point for <see cref="ICompletionLauncher"/> and <see cref="MergeTrackingService"/>). It acts
/// on the spec's persisted state, so repeated or stale triggers are harmless:
/// <list type="bullet">
/// <item><c>Testing</c> with a passing tester verdict for the current test cycle: verify the PR stack
/// (<see cref="StackVerifier"/>), mark every draft PR ready for review, and move to <c>ReadyForReview</c> (which frees the
/// active-spec slot), then clean up the implementer worktrees, report the integration branch, and move to
/// <c>AwaitingMerge</c>. Without any PR layer, the integration branch is pushed and reported on the spec issue, the
/// integrated tickets are closed, and the spec completes. A failed verification needs attention and keeps the worktrees.</item>
/// <item><c>ReadyForReview</c> (left behind by a crash): finish marking ready, clean up, report, and await merge.</item>
/// <item><c>Aborted</c>: clean up the implementer worktrees.</item>
/// </list>
/// Git/GitHub failures leave the spec unchanged (<see cref="CompletionOutcome.Faulted"/>); the next tracking pass resumes it.
/// Publishing is serialized per repository with the integration sagas.
/// </summary>
public sealed class SpecCompletionService(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IPullStackLayerRepository layers,
    IRunEventRepository runEvents,
    IGitWorkspace git,
    IGitHubPullsAndStacks pulls,
    IGitHubIssues issues,
    RepositoryIntegrationGate gate,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private readonly SpecRunJournal _journal = new(outbox, clock);
    private readonly StackVerifier _verifier = new(git, pulls);
    private readonly ImplementerWorktreeCleaner _cleaner = new(ticketRuns, stepRuns, runEvents, git, outbox, clock);

    public async Task<CompletionResult> RunAsync(CompletionAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        SpecRun? spec = await specRuns.GetAsync(assignment.SpecRunId, cancellationToken);
        if (spec is not { Status: SpecRunStatus.Testing or SpecRunStatus.ReadyForReview or SpecRunStatus.Aborted })
        {
            return CompletionResult.NothingToDo;
        }

        RepositoryRecord? repository = await repositories.GetAsync(spec.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return CompletionResult.NothingToDo;
        }

        var location = GitRepositoryLocation.From(repository);
        try
        {
            using (await gate.EnterAsync(spec.RepositoryId, cancellationToken))
            {
                return spec.Status switch
                {
                    SpecRunStatus.Testing => await CompleteTestedAsync(spec, location, cancellationToken),
                    SpecRunStatus.ReadyForReview => await FinishReadyAsync(spec, location, cancellationToken),
                    _ => await CleanUpAbortedAsync(spec, location, cancellationToken),
                };
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new CompletionResult(CompletionOutcome.Faulted, exception.Message);
        }
    }

    private async Task<CompletionResult> CompleteTestedAsync(SpecRun spec, GitRepositoryLocation location, CancellationToken cancellationToken)
    {
        if (PassedHead(await stepRuns.ListBySpecRunAsync(spec.Id, cancellationToken), spec.TestCycle) is not { } tested)
        {
            return CompletionResult.NothingToDo;
        }

        CommitSha? tip = await git.GetBranchTipAsync(location, spec.IntegrationBranch, GitRefScope.Local, cancellationToken) ?? spec.IntegrationTipSha;
        if (tip != tested)
        {
            return await NeedsAttentionAsync(
                spec,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The integration tip {tip?.Value ?? "(missing)"} was not tested: test cycle {spec.TestCycle} passed {tested}."),
                cancellationToken);
        }

        IReadOnlyList<PullStackLayer> stack = await layers.ListBySpecRunAsync(spec.Id, cancellationToken);
        if (stack.Count == 0)
        {
            return await CompleteWithoutPullRequestsAsync(spec, location, tested, cancellationToken);
        }

        if (await _verifier.FindProblemAsync(spec, location, stack, tested, cancellationToken) is { } problem)
        {
            return await NeedsAttentionAsync(spec, $"Stack verification failed before marking the PRs ready: {problem}", cancellationToken);
        }

        await MarkReadyAsync(location.Repo, stack, cancellationToken);
        _journal.Move(spec, SpecRunStatus.ReadyForReview);
        return await SaveAsync(cancellationToken)
            ? await AwaitMergeAsync(spec, location, stack, cancellationToken)
            : CompletionResult.ConcurrencyConflict;
    }

    /// <summary>The head the tester passed in <paramref name="testCycle"/>, or null while that cycle has no passing verdict.</summary>
    private static CommitSha? PassedHead(IReadOnlyList<StepRun> steps, int testCycle)
    {
        if (steps.Any(step => step is { Kind: StepKind.Test, IsActive: true }))
        {
            return null;
        }

        TestStepRecord? latest = steps
            .Where(step => step is { Kind: StepKind.Test, Status: StepStatus.Succeeded, TicketRunId: null })
            .OrderByDescending(step => step.Attempt)
            .Select(step => TestStepRecord.TryParse(step.StructuredResultJson))
            .FirstOrDefault(record => record?.TestCycle == testCycle);
        return latest is { Verdict: TestVerdict.Pass } ? new CommitSha(latest.TestedHead) : null;
    }

    private async Task<CompletionResult> FinishReadyAsync(SpecRun spec, GitRepositoryLocation location, CancellationToken cancellationToken)
    {
        IReadOnlyList<PullStackLayer> stack = await layers.ListBySpecRunAsync(spec.Id, cancellationToken);
        if (stack.Count == 0)
        {
            return await NeedsAttentionAsync(spec, $"Spec run '{spec.Id}' is ready for review but has no PR stack to track.", cancellationToken);
        }

        await MarkReadyAsync(location.Repo, stack, cancellationToken);
        return await AwaitMergeAsync(spec, location, stack, cancellationToken);
    }

    private async Task<CompletionResult> AwaitMergeAsync(
        SpecRun spec,
        GitRepositoryLocation location,
        IReadOnlyList<PullStackLayer> stack,
        CancellationToken cancellationToken)
    {
        await _cleaner.CleanAsync(spec, location, cancellationToken);
        outbox.Append(new SpecCompletionReported(spec.Id, spec.RepositoryId, spec.IntegrationBranch, stack[^1].CommitSha, stack.Count, clock.UtcNow));
        _journal.Move(spec, SpecRunStatus.AwaitingMerge);
        return await SaveAsync(cancellationToken) ? new CompletionResult(CompletionOutcome.AwaitingMerge) : CompletionResult.ConcurrencyConflict;
    }

    /// <summary>Idempotent: PRs that are already ready for review are left alone.</summary>
    private async Task MarkReadyAsync(GitHubRepoRef repository, IReadOnlyList<PullStackLayer> stack, CancellationToken cancellationToken)
    {
        foreach (PullStackLayer layer in stack)
        {
            PullRequestSnapshot pull = await pulls.GetPullRequestAsync(repository, layer.PullRequestNumber, cancellationToken);
            if (pull.IsDraft)
            {
                await pulls.MarkReadyForReviewAsync(repository, layer.PullRequestNumber, cancellationToken);
            }

            if (layer.IsDraft)
            {
                layer.MarkReady(clock.UtcNow);
            }
        }
    }

    /// <summary>
    /// The no-PR path of step 13: the integration branch is the deliverable, so it is pushed and reported on the spec issue,
    /// and each integrated ticket is resolved the way the tracker closes work.
    /// </summary>
    private async Task<CompletionResult> CompleteWithoutPullRequestsAsync(
        SpecRun spec,
        GitRepositoryLocation location,
        CommitSha tip,
        CancellationToken cancellationToken)
    {
        await git.FetchAsync(location, cancellationToken);
        CommitSha? remote = await git.GetBranchTipAsync(location, spec.IntegrationBranch, GitRefScope.Remote, cancellationToken);
        if (remote != tip
            && await git.PushAsync(location, new RefPush(spec.IntegrationBranch, tip, remote), cancellationToken) == PushOutcome.Rejected)
        {
            return await NeedsAttentionAsync(spec, $"Pushing the integration branch '{spec.IntegrationBranch}' at {tip} was rejected.", cancellationToken);
        }

        TicketRun[] integrated = (await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken))
            .Where(ticket => ticket.Status == TicketRunStatus.Integrated)
            .OrderBy(ticket => ticket.Issue.Number)
            .ToArray();
        foreach (TicketRun ticket in integrated)
        {
            if ((await issues.GetIssueAsync(ticket.Issue, cancellationToken)).State != IssueState.Closed)
            {
                await issues.CloseAsync(ticket.Issue, IssueCloseReason.Completed, cancellationToken);
            }
        }

        await issues.CommentAsync(spec.ParentIssue, IntegrationBranchReport(spec, tip, integrated), cancellationToken);
        await _cleaner.CleanAsync(spec, location, cancellationToken);
        outbox.Append(new SpecCompletionReported(spec.Id, spec.RepositoryId, spec.IntegrationBranch, tip, PullRequestCount: 0, clock.UtcNow));
        _journal.Move(spec, SpecRunStatus.Completed);
        return await SaveAsync(cancellationToken)
            ? new CompletionResult(CompletionOutcome.CompletedWithoutPullRequests)
            : CompletionResult.ConcurrencyConflict;
    }

    private static string IntegrationBranchReport(SpecRun spec, CommitSha tip, IReadOnlyList<TicketRun> integrated)
    {
        string tickets = integrated.Count == 0 ? "none" : string.Join(", ", integrated.Select(ticket => $"#{ticket.Issue.Number}"));
        return $"""
            WebDevLoop finished spec run `{spec.Id}`: the parent-spec review and the tester passed. No pull requests were published, so the integrated tickets were closed directly.

            Integration branch: `{spec.IntegrationBranch}` at `{tip}`
            Resolved tickets: {tickets}
            """;
    }

    private async Task<CompletionResult> CleanUpAbortedAsync(SpecRun spec, GitRepositoryLocation location, CancellationToken cancellationToken)
    {
        await _cleaner.CleanAsync(spec, location, cancellationToken);
        return await SaveAsync(cancellationToken) ? new CompletionResult(CompletionOutcome.CleanedUp) : CompletionResult.ConcurrencyConflict;
    }

    private async Task<CompletionResult> NeedsAttentionAsync(SpecRun spec, string reason, CancellationToken cancellationToken)
    {
        _journal.MarkNeedsAttention(spec, reason);
        return await SaveAsync(cancellationToken) ? new CompletionResult(CompletionOutcome.NeedsAttention, reason) : CompletionResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
}
