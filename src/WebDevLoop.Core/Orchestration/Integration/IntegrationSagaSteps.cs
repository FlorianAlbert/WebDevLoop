using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Executes workflow step 7 for one ticket as a checkpointed saga. Every external side effect is followed by a persisted
/// checkpoint, and every step is replay-safe (compare-and-swap ref updates, leased pushes, PR lookup by exact head ref,
/// stack membership lookup, issue state check), so a crash at any point resumes without duplicating commits, PRs, or
/// issue transitions. Order: squash onto the expected prior tip (conflict resolver only on conflicts, then retry) → move
/// the integration ref → push it → push the immutable <c>stack/&lt;run&gt;/&lt;ticket&gt;</c> ref → create the draft PR →
/// link it into the GitHub stack → verify its diff → close the ticket issue → mark the ticket integrated, which recomputes
/// the frontier (step 8). The caller holds the repository merge lock.
/// </summary>
public sealed class IntegrationSagaSteps(
    ISpecRunRepository specRuns,
    IPullStackLayerRepository layers,
    IGitWorkspace git,
    IGitHubPullsAndStacks pulls,
    IGitHubIssues issues,
    ConflictResolutionRunner conflicts,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    internal async Task<IntegrationResult> AdvanceAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        try
        {
            RetargetIfStale(context);
            while (!context.Saga.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await StepAsync(context, cancellationToken) is { } stop)
                {
                    return stop;
                }
            }

            return IntegrationResult.Integrated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.Saga.RecordError(exception.Message, clock.UtcNow);
            return await SaveAsync(cancellationToken)
                ? new IntegrationResult(IntegrationOutcome.Faulted, exception.Message)
                : IntegrationResult.ConcurrencyConflict;
        }
    }

    /// <returns>Null to continue with the next checkpoint; otherwise why the saga stops.</returns>
    private Task<IntegrationResult?> StepAsync(IntegrationContext context, CancellationToken cancellationToken) =>
        context.Saga.Checkpoint switch
        {
            IntegrationSagaCheckpoint.Started => SquashAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.SquashCommitCreated => UpdateIntegrationRefAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.IntegrationRefUpdated => PushIntegrationBranchAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.IntegrationPushed => PushStackBranchAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.StackBranchPushed => CreatePullRequestAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.PrCreated => LinkStackAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.StackLinked => VerifyDiffAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.DiffVerified => TransitionIssueAsync(context, cancellationToken),
            IntegrationSagaCheckpoint.IssueTransitioned => CompleteAsync(context, cancellationToken),
            _ => throw new InvalidOperationException($"Integration saga checkpoint {context.Saga.Checkpoint} has no step."),
        };

    /// <summary>
    /// A saga that has not moved the integration ref yet, while another ticket advanced the tip (e.g. it failed and was
    /// retried later), squashes again onto the current tip.
    /// </summary>
    private void RetargetIfStale(IntegrationContext context)
    {
        IntegrationSaga saga = context.Saga;
        if (saga.Checkpoint < IntegrationSagaCheckpoint.IntegrationRefUpdated
            && context.Spec.IntegrationTipSha is { } tip
            && saga.ExpectedPriorIntegrationSha != tip)
        {
            DateTimeOffset now = clock.UtcNow;
            saga.RetargetTo(tip, now);
            outbox.Append(new SagaCheckpointAdvanced(saga.SpecRunId, saga.TicketRunId, saga.Checkpoint, now));
        }
    }

    private async Task<IntegrationResult?> SquashAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        if (context.Ticket.LastImplementedSha is not { } source)
        {
            return await NeedsAttentionAsync(context, $"Ticket '{context.Ticket.Id}' has no reviewed commit to integrate.", cancellationToken);
        }

        CommitSha tip = context.ExpectedPrior;
        GitMergeResult squash = await git.CreateSquashCommitAsync(
            context.Location, new SquashRequest(source, tip, StackLayerPullRequest.SquashMessage(context)), cancellationToken);
        switch (squash.Outcome)
        {
            case GitMergeOutcome.Merged:
                context.Saga.SquashCommitSha = squash.Commit;
                return await CheckpointAsync(context, IntegrationSagaCheckpoint.SquashCommitCreated, cancellationToken);
            case GitMergeOutcome.AlreadyUpToDate:
                return await NeedsAttentionAsync(
                    context, $"Ticket branch '{context.Ticket.BranchName}' at {source} has no changes relative to the integration tip {tip}.", cancellationToken);
            default:
                return await ResolveConflictsAsync(context, source, squash.ConflictedPaths, cancellationToken);
        }
    }

    /// <returns>Null when the resolver merged the integration tip into the ticket branch, so the squash is retried.</returns>
    private async Task<IntegrationResult?> ResolveConflictsAsync(
        IntegrationContext context,
        CommitSha source,
        IReadOnlyList<string> conflictedPaths,
        CancellationToken cancellationToken)
    {
        StackPlacement placement = await PlaceAsync(context, cancellationToken);
        IReadOnlyList<string> changedFiles = await TicketChangedFilesAsync(context, placement, source, cancellationToken);
        ConflictResolution resolution = await conflicts.ResolveAsync(context, context.ExpectedPrior, conflictedPaths, changedFiles, cancellationToken);
        return resolution.Outcome switch
        {
            ConflictResolutionOutcome.Resolved => null,
            ConflictResolutionOutcome.Cancelled => new IntegrationResult(IntegrationOutcome.Cancelled, resolution.Reason),
            ConflictResolutionOutcome.ConcurrencyConflict => IntegrationResult.ConcurrencyConflict,
            _ => await NeedsAttentionAsync(context, resolution.Reason!, cancellationToken),
        };
    }

    private async Task<IntegrationResult?> UpdateIntegrationRefAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        CommitSha squash = context.SquashCommit;
        RefUpdateResult update = await git.UpdateBranchAsync(
            context.Location, context.Spec.IntegrationBranch, squash, context.ExpectedPrior, cancellationToken);
        if (!update.Succeeded)
        {
            return await NeedsAttentionAsync(
                context,
                $"Integration branch '{context.Spec.IntegrationBranch}' is at {update.ActualTip?.Value ?? "(missing)"} instead of the expected prior tip {context.ExpectedPrior}.",
                cancellationToken);
        }

        context.Spec.IntegrationTipSha = squash;
        context.Ticket.IntegratedCommitSha = squash;
        return await CheckpointAsync(context, IntegrationSagaCheckpoint.IntegrationRefUpdated, cancellationToken);
    }

    /// <summary>The run-scoped integration branch is first pushed with the first layer; later pushes lease the previous tip.</summary>
    private async Task<IntegrationResult?> PushIntegrationBranchAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        CommitSha? expectedRemote = context.ExpectedPrior == context.Spec.IntegrationBaseSha ? null : context.ExpectedPrior;
        PushOutcome push = await git.PushAsync(
            context.Location, new RefPush(context.Spec.IntegrationBranch, context.SquashCommit, expectedRemote), cancellationToken);
        return push == PushOutcome.Rejected
            ? await NeedsAttentionAsync(
                context,
                $"Pushing '{context.Spec.IntegrationBranch}' was rejected: the remote branch is not at {expectedRemote?.Value ?? "(absent)"}.",
                cancellationToken)
            : await CheckpointAsync(context, IntegrationSagaCheckpoint.IntegrationPushed, cancellationToken);
    }

    private async Task<IntegrationResult?> PushStackBranchAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        BranchName stackBranch = context.Saga.StackBranchName;
        PushOutcome push = await git.PushAsync(context.Location, new RefPush(stackBranch, context.SquashCommit, null), cancellationToken);
        return push == PushOutcome.Rejected
            ? await NeedsAttentionAsync(context, $"Stack branch '{stackBranch}' already exists on the remote at another commit.", cancellationToken)
            : await CheckpointAsync(context, IntegrationSagaCheckpoint.StackBranchPushed, cancellationToken);
    }

    private async Task<IntegrationResult?> CreatePullRequestAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        StackPlacement placement = await PlaceAsync(context, cancellationToken);
        GitHubRepoRef repository = context.Repository.Ref;
        PullRequestSnapshot pull = await pulls.FindPullRequestByHeadAsync(repository, context.Saga.StackBranchName, cancellationToken)
            ?? await pulls.CreateDraftPullRequestAsync(repository, StackLayerPullRequest.Draft(context, placement.BaseBranch), cancellationToken);
        if (pull.State != PullRequestState.Open)
        {
            return await NeedsAttentionAsync(context, $"Pull request #{pull.Number} for '{pull.Head}' is {pull.State}.", cancellationToken);
        }

        if (placement.Own is null)
        {
            layers.Add(PullStackLayer.Create(
                context.Spec.Id, context.Ticket.Id, context.Saga.StackBranchName, context.SquashCommit, pull.Number, placement.BaseBranch, placement.Position, clock.UtcNow));
        }

        context.Saga.PullRequestNumber = pull.Number;
        context.Ticket.PullRequestNumber = pull.Number;
        context.Ticket.StackPosition = placement.Position;
        return await CheckpointAsync(context, IntegrationSagaCheckpoint.PrCreated, cancellationToken);
    }

    /// <summary>
    /// Puts the PR directly above the layer below it. GitHub stacks need at least two PRs, so a bottom PR on trunk stays an
    /// ordinary PR until the second layer creates the stack.
    /// </summary>
    private async Task<IntegrationResult?> LinkStackAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        StackPlacement placement = await PlaceAsync(context, cancellationToken);
        PullStackLayer own = placement.Own ?? throw new InvalidOperationException($"Stack layer of ticket '{context.Ticket.Id}' is missing.");
        if (placement.Below is not { } below)
        {
            return await CheckpointAsync(context, IntegrationSagaCheckpoint.StackLinked, cancellationToken);
        }

        GitHubRepoRef repository = context.Repository.Ref;
        PullRequestNumber pull = own.PullRequestNumber;
        PullStackSnapshot? stack = await pulls.FindStackAsync(repository, pull, cancellationToken);
        if (stack is null)
        {
            PullStackSnapshot? belowStack = await pulls.FindStackAsync(repository, below.PullRequestNumber, cancellationToken);
            if (belowStack is not null && belowStack.BottomToTop[^1] != below.PullRequestNumber)
            {
                return await NeedsAttentionAsync(
                    context, $"Stack {belowStack.StackNumber} does not end with #{below.PullRequestNumber}, the layer below #{pull}.", cancellationToken);
            }

            stack = belowStack is null
                ? await pulls.CreateStackAsync(repository, [below.PullRequestNumber, pull], cancellationToken)
                : await pulls.AddToStackAsync(repository, belowStack.StackNumber, pull, cancellationToken);
        }

        int index = IndexOf(stack.BottomToTop, pull);
        if (index < 1 || stack.BottomToTop[index - 1] != below.PullRequestNumber)
        {
            return await NeedsAttentionAsync(
                context, $"Pull request #{pull} is not directly above #{below.PullRequestNumber} in stack {stack.StackNumber}.", cancellationToken);
        }

        context.Saga.StackNumber = stack.StackNumber;
        own.StackNumber = stack.StackNumber;
        below.StackNumber ??= stack.StackNumber;
        return await CheckpointAsync(context, IntegrationSagaCheckpoint.StackLinked, cancellationToken);
    }

    private async Task<IntegrationResult?> VerifyDiffAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        await git.FetchAsync(context.Location, cancellationToken);
        StackPlacement placement = await PlaceAsync(context, cancellationToken);
        PullStackLayer own = placement.Own ?? throw new InvalidOperationException($"Stack layer of ticket '{context.Ticket.Id}' is missing.");
        if (await FindDiffProblemAsync(context, placement, own, cancellationToken) is { } problem)
        {
            return await NeedsAttentionAsync(context, $"Diff verification of pull request #{own.PullRequestNumber} failed: {problem}", cancellationToken);
        }

        own.RecordVerifiedDiff(context.SquashCommit, clock.UtcNow);
        return await CheckpointAsync(context, IntegrationSagaCheckpoint.DiffVerified, cancellationToken);
    }

    /// <summary>
    /// The PR diff is exactly the squash commit when the PR heads the stack branch at that commit and its base branch contains
    /// the commit's parent but not the commit itself (GitHub diffs against the merge base). The squash commit itself may only
    /// touch files the ticket branch changed.
    /// </summary>
    /// <returns>Null when the PR shows only this ticket's changes; otherwise what is wrong.</returns>
    private async Task<string?> FindDiffProblemAsync(IntegrationContext context, StackPlacement placement, PullStackLayer own, CancellationToken cancellationToken)
    {
        CommitSha squash = context.SquashCommit;
        CommitSha prior = context.ExpectedPrior;
        PullRequestSnapshot pull = await pulls.GetPullRequestAsync(context.Repository.Ref, own.PullRequestNumber, cancellationToken);
        if (pull.State != PullRequestState.Open || pull.Head != context.Saga.StackBranchName || pull.HeadSha != squash || pull.Base != own.BaseBranch)
        {
            return $"the PR is {pull.State} '{pull.Head}' at {pull.HeadSha} onto '{pull.Base}', expected open '{context.Saga.StackBranchName}' at {squash} onto '{own.BaseBranch}'.";
        }

        CommitSha? baseTip = await git.GetBranchTipAsync(context.Location, own.BaseBranch, GitRefScope.Remote, cancellationToken);
        if (baseTip is not { } baseHead
            || !await git.IsAncestorAsync(context.Location, prior, baseHead, cancellationToken)
            || await git.IsAncestorAsync(context.Location, squash, baseHead, cancellationToken))
        {
            return $"base '{own.BaseBranch}' at {baseTip?.Value ?? "(missing)"} must contain the parent {prior} but not the layer commit {squash}.";
        }

        IReadOnlyList<string> layerFiles = await git.GetChangedFilesAsync(context.Location, prior, squash, cancellationToken);
        if (layerFiles.Count == 0)
        {
            return $"the layer commit {squash} changes no files.";
        }

        IReadOnlyList<string> ticketFiles = await TicketChangedFilesAsync(context, placement, context.Ticket.LastImplementedSha!.Value, cancellationToken);
        string[] foreign = layerFiles.Except(ticketFiles, StringComparer.Ordinal).ToArray();
        return foreign.Length == 0
            ? null
            : $"the layer commit changes files the ticket branch does not: {string.Join(", ", foreign)}.";
    }

    /// <summary>Links the PR to its ticket by closing the issue through the tracker; the PR body already references it.</summary>
    private async Task<IntegrationResult?> TransitionIssueAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        IssueSnapshot issue = await issues.GetIssueAsync(context.Ticket.Issue, cancellationToken);
        if (issue.State != IssueState.Closed)
        {
            await issues.CloseAsync(context.Ticket.Issue, IssueCloseReason.Completed, cancellationToken);
        }

        return await CheckpointAsync(context, IntegrationSagaCheckpoint.IssueTransitioned, cancellationToken);
    }

    /// <summary>The ticket's <c>Integrated</c> event recomputes the frontier (step 8) through the outbox.</summary>
    private async Task<IntegrationResult?> CompleteAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRun ticket = context.Ticket;
        ticket.TransitionTo(TicketRunStatus.Integrated, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, TicketRunStatus.Integrating, TicketRunStatus.Integrated, now));
        return await CheckpointAsync(context, IntegrationSagaCheckpoint.Completed, cancellationToken);
    }

    private async Task<StackPlacement> PlaceAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        SpecRun spec = context.Spec;
        IReadOnlyList<PullStackLayer> specLayers = await layers.ListBySpecRunAsync(spec.Id, cancellationToken);
        PullStackLayer? own = specLayers.FirstOrDefault(layer => layer.TicketRunId == context.Ticket.Id);
        PullStackLayer? previous = specLayers.LastOrDefault(layer => layer.TicketRunId != context.Ticket.Id && (own is null || layer.Position < own.Position));
        PullStackLayer? blockingTop = previous is null ? await FindBlockingTopLayerAsync(spec, cancellationToken) : null;
        BranchName baseBranch = own?.BaseBranch ?? PullRequestBasePlanner.PlanBase(
            spec.DependencyModeUsed ?? SpecDependencyMode.WaitForMerge, context.Trunk, blockingTop?.BranchName, previous?.BranchName);
        int position = own?.Position ?? (previous?.Position ?? 0) + 1;
        return new StackPlacement(own, previous ?? blockingTop, baseBranch, position, specLayers);
    }

    /// <summary>For a stack-on-top spec, the blocking spec's top layer whose commit the integration branch started from.</summary>
    private async Task<PullStackLayer?> FindBlockingTopLayerAsync(SpecRun spec, CancellationToken cancellationToken)
    {
        if (spec.DependencyModeUsed != SpecDependencyMode.StackOnTop || spec.IntegrationBaseSha is not { } baseSha)
        {
            return null;
        }

        foreach (SpecDependency dependency in await specRuns.ListDependenciesAsync(spec.Id, cancellationToken))
        {
            foreach (SpecRun blocker in await BlockingRunsAsync(spec, dependency, cancellationToken))
            {
                IReadOnlyList<PullStackLayer> blockerLayers = await layers.ListBySpecRunAsync(blocker.Id, cancellationToken);
                if (blockerLayers.Count > 0 && blockerLayers[^1].CommitSha == baseSha)
                {
                    return blockerLayers[^1];
                }
            }
        }

        return null;
    }

    private async Task<IEnumerable<SpecRun>> BlockingRunsAsync(SpecRun spec, SpecDependency dependency, CancellationToken cancellationToken)
    {
        if (dependency.BlockingSpecRunId is { } blockingId)
        {
            return await specRuns.GetAsync(blockingId, cancellationToken) is { } blocker ? [blocker] : [];
        }

        IssueRef external = dependency.ExternalBlockingIssue!.Value;
        return (await specRuns.ListByRepositoryAsync(spec.RepositoryId, cancellationToken))
            .Where(run => run.Id != spec.Id && SpecQueue.SpecIssues.AreSame(run.ParentIssue, external));
    }

    /// <summary>
    /// Files the ticket branch changes on top of the newest integration commit it contains. Integration history is the
    /// run's base plus its layer commits, so those are the only candidates.
    /// </summary>
    private async Task<IReadOnlyList<string>> TicketChangedFilesAsync(
        IntegrationContext context,
        StackPlacement placement,
        CommitSha ticketHead,
        CancellationToken cancellationToken)
    {
        IEnumerable<CommitSha> candidates = new[] { context.ExpectedPrior }
            .Concat(placement.SpecLayers.Where(layer => layer.TicketRunId != context.Ticket.Id).Reverse().Select(layer => layer.CommitSha))
            .Concat(context.Spec.IntegrationBaseSha is { } baseSha ? [baseSha] : [])
            .Distinct();
        foreach (CommitSha candidate in candidates)
        {
            if (await git.IsAncestorAsync(context.Location, candidate, ticketHead, cancellationToken))
            {
                return await git.GetChangedFilesAsync(context.Location, candidate, ticketHead, cancellationToken);
            }
        }

        return [];
    }

    private static int IndexOf(IReadOnlyList<PullRequestNumber> bottomToTop, PullRequestNumber pull)
    {
        for (int index = 0; index < bottomToTop.Count; index++)
        {
            if (bottomToTop[index] == pull)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task<IntegrationResult?> CheckpointAsync(IntegrationContext context, IntegrationSagaCheckpoint next, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        context.Saga.AdvanceTo(next, now);
        outbox.Append(new SagaCheckpointAdvanced(context.Saga.SpecRunId, context.Saga.TicketRunId, next, now));
        return await SaveAsync(cancellationToken) ? null : IntegrationResult.ConcurrencyConflict;
    }

    private async Task<IntegrationResult> NeedsAttentionAsync(IntegrationContext context, string reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRun ticket = context.Ticket;
        TicketRunStatus previous = ticket.Status;
        context.Saga.RecordError(reason, now);
        ticket.MarkNeedsAttention(reason, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, TicketRunStatus.NeedsAttention, now));
        return await SaveAsync(cancellationToken)
            ? new IntegrationResult(IntegrationOutcome.NeedsAttention, reason)
            : IntegrationResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
}
