using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Tracks the human merge of ready PR stacks (periodic entry point <see cref="TrackAllAsync"/>, run by a hosted timer).
/// A stack merged into trunk (trunk contains the top layer) completes its spec, which unblocks <c>WaitForMerge</c>
/// dependents; a stack closed unmerged needs attention. When every PR is merged but trunk does not contain the top layer,
/// the first such poll is recorded (run event <see cref="AwaitingTrunkRunEventType"/>) and the spec needs attention once
/// trunk still lacks it after <see cref="ReadyAndMergeOptions.TrunkContainmentTimeout"/>. The pass also resumes
/// completions that a fault or crash interrupted (<c>Testing</c> with a passing verdict, or <c>ReadyForReview</c>).
/// </summary>
public sealed class MergeTrackingService(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IPullStackLayerRepository layers,
    IRunEventRepository runEvents,
    IGitHubPullsAndStacks pulls,
    SpecCompletionService completion,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    ReadyAndMergeOptions options)
{
    public const string AwaitingTrunkRunEventType = "StackMergedAwaitingTrunk";

    private readonly SpecRunJournal _journal = new(outbox, clock);

    /// <summary>One pass over every non-terminal spec; a failing spec does not stop the others.</summary>
    public async Task<MergeTrackingPass> TrackAllAsync(CancellationToken cancellationToken)
    {
        var completions = new Dictionary<RunId, CompletionResult>();
        var merges = new Dictionary<RunId, MergeTrackingResult>();
        foreach (SpecRun spec in await specRuns.ListNonTerminalAsync(cancellationToken))
        {
            switch (spec.Status)
            {
                case SpecRunStatus.Testing or SpecRunStatus.ReadyForReview:
                    CompletionResult resumed = await completion.RunAsync(new CompletionAssignment(spec.Id), cancellationToken);
                    if (resumed.Outcome != CompletionOutcome.NothingToDo)
                    {
                        completions[spec.Id] = resumed;
                    }

                    break;
                case SpecRunStatus.AwaitingMerge:
                    merges[spec.Id] = await TrackAsync(spec.Id, cancellationToken);
                    break;
            }
        }

        return new MergeTrackingPass(completions, merges);
    }

    public async Task<MergeTrackingResult> TrackAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        SpecRun? spec = await specRuns.GetAsync(specRunId, cancellationToken);
        if (spec is not { Status: SpecRunStatus.AwaitingMerge }
            || await repositories.GetAsync(spec.RepositoryId, cancellationToken) is not { } repository)
        {
            return MergeTrackingResult.NotTracked;
        }

        IReadOnlyList<PullStackLayer> stack = await layers.ListBySpecRunAsync(spec.Id, cancellationToken);
        if (stack.Count == 0)
        {
            return await NeedsAttentionAsync(
                spec, AttentionReasons.InternalInconsistency($"Spec run '{spec.Id}' awaits merge but has no PR stack to track.", forTicket: false), cancellationToken);
        }

        BranchName trunk = spec.BaseBranch ?? repository.DefaultBaseBranch;
        PullRequestNumber[] bottomToTop = stack.Select(layer => layer.PullRequestNumber).ToArray();
        try
        {
            StackMergeStatus status = await pulls.GetStackMergeStatusAsync(repository.Ref, bottomToTop, trunk, cancellationToken);
            return status switch
            {
                StackMergeStatus.Merged => await CompleteAsync(spec, cancellationToken),
                StackMergeStatus.ClosedUnmerged => await NeedsAttentionAsync(
                    spec,
                    AttentionReasons.PullRequestsClosedUnmerged(PullRequestNumbers.Describe(bottomToTop), trunk.Value, repository.Ref.ToString()),
                    cancellationToken),
                _ => await AllMergedAsync(repository.Ref, bottomToTop, cancellationToken)
                    ? await AwaitTrunkAsync(spec, stack, trunk, cancellationToken)
                    : new MergeTrackingResult(MergeTrackingOutcome.Open),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MergeTrackingResult(MergeTrackingOutcome.Faulted, exception.Message);
        }
    }

    private async Task<bool> AllMergedAsync(GitHubRepoRef repository, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken)
    {
        foreach (PullRequestNumber number in bottomToTop)
        {
            if ((await pulls.GetPullRequestAsync(repository, number, cancellationToken)).State != PullRequestState.Merged)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<MergeTrackingResult> AwaitTrunkAsync(
        SpecRun spec,
        IReadOnlyList<PullStackLayer> stack,
        BranchName trunk,
        CancellationToken cancellationToken)
    {
        PullStackLayer top = stack[^1];
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset? since = (await runEvents.ListBySpecRunAsync(spec.Id, cancellationToken))
            .Where(runEvent => runEvent.Type == AwaitingTrunkRunEventType && runEvent.OccurredAt >= spec.ReadyAt)
            .Select(runEvent => (DateTimeOffset?)runEvent.OccurredAt)
            .Min();
        if (since is null)
        {
            string payload = JsonSerializer.Serialize(new { topPullRequest = top.PullRequestNumber.Value, topCommit = top.CommitSha.Value });
            runEvents.Add(RunEvent.Create(spec.Id, null, AwaitingTrunkRunEventType, payload, now));
            outbox.Append(new SpecStackAwaitingTrunk(spec.Id, spec.RepositoryId, top.PullRequestNumber, now));
            return await SaveAsync(cancellationToken) ? new MergeTrackingResult(MergeTrackingOutcome.AwaitingTrunk) : MergeTrackingResult.ConcurrencyConflict;
        }

        TimeSpan timeout = options.TrunkContainmentTimeout;
        return now - since.Value < timeout
            ? new MergeTrackingResult(MergeTrackingOutcome.AwaitingTrunk)
            : await NeedsAttentionAsync(
                spec,
                AttentionReasons.TrunkMissingStack(
                    $"Every PR of the stack {PullRequestNumbers.Describe(stack.Select(layer => layer.PullRequestNumber))} is merged, but '{trunk}' does not contain the top layer "
                        + $"#{top.PullRequestNumber} ({top.CommitSha}) {timeout:c} after the merge was first seen.",
                    trunk.Value),
                cancellationToken);
    }

    private async Task<MergeTrackingResult> CompleteAsync(SpecRun spec, CancellationToken cancellationToken)
    {
        _journal.Move(spec, SpecRunStatus.Completed);
        return await SaveAsync(cancellationToken) ? new MergeTrackingResult(MergeTrackingOutcome.Completed) : MergeTrackingResult.ConcurrencyConflict;
    }

    private async Task<MergeTrackingResult> NeedsAttentionAsync(SpecRun spec, AttentionReason reason, CancellationToken cancellationToken)
    {
        _journal.MarkNeedsAttention(spec, reason);
        return await SaveAsync(cancellationToken)
            ? new MergeTrackingResult(MergeTrackingOutcome.NeedsAttention, reason.Details)
            : MergeTrackingResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
}
