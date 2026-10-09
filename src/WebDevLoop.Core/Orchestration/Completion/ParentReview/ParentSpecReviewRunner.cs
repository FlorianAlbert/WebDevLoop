using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>
/// Workflow steps 9–10 for a spec in <c>ParentReviewing</c> (entry point for <see cref="IParentReviewLauncher"/>): runs the
/// same two independent review axes as ticket reviews (<see cref="TwoAxisReviewRunner"/>, scope parent spec) against a
/// read-only checkout of the integration tip, diffed against the integration base. Clean → <c>Testing</c>. Findings become
/// finding tickets (<see cref="FindingTicketIssuer"/>) and the spec returns to <c>Running</c>, so the new frontier is
/// worked through the normal ticket flow and, once every ticket is done, <see cref="ParentReviewStarter"/> starts the next
/// cycle. Loops are bounded: findings in the last allowed cycle (<c>ParentReviewCycleLimit</c>), or findings that only
/// repeat already-done tickets, move the spec to <c>NeedsAttention</c>. Axis results are persisted with the review steps,
/// so a restarted runner resumes the round, and finding issuance is idempotent.
/// </summary>
public sealed class ParentSpecReviewRunner(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IEffectiveSettingsProvider settings,
    IGitWorkspace git,
    TwoAxisReviewRunner reviews,
    FindingTicketIssuer findings,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private readonly SpecRunJournal _journal = new(outbox, clock);

    public async Task<ParentReviewResult> RunAsync(ParentReviewAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        SpecRun? spec = await specRuns.GetAsync(assignment.SpecRunId, cancellationToken);
        if (spec is not { Status: SpecRunStatus.ParentReviewing })
        {
            return ParentReviewResult.NotParentReviewing;
        }

        if ((await stepRuns.ListBySpecRunAsync(spec.Id, cancellationToken)).Any(step => step is { Kind: StepKind.ParentReview, IsActive: true }))
        {
            return ParentReviewResult.AlreadyRunning;
        }

        if (await LoadContextAsync(spec, cancellationToken) is not { } context)
        {
            return await NeedsAttentionAsync(
                spec, ParentReviewOutcome.Failed, $"Repository, integration base, or integration tip of spec run '{spec.Id}' is missing.", [], cancellationToken);
        }

        ReviewRound round = ParentReviewRounds.Current(spec);
        IReadOnlyDictionary<FindingAxis, ParentReviewAxisResult> results = await ReadRoundAsync(context, round, cancellationToken);
        FindingAxis[] missing = ReviewRequest.BothAxes.Where(axis => !results.ContainsKey(axis)).ToArray();
        if (missing.Length > 0)
        {
            ReviewRoundResult review = await ReviewAsync(context, round, missing, cancellationToken);
            switch (review.Outcome)
            {
                case ReviewRoundOutcome.Cancelled:
                    return new ParentReviewResult(ParentReviewOutcome.Cancelled, [], review.Reason);
                case ReviewRoundOutcome.ConcurrencyConflict:
                    return ParentReviewResult.ConcurrencyConflict;
                case ReviewRoundOutcome.Failed:
                    return await NeedsAttentionAsync(spec, ParentReviewOutcome.Failed, review.Reason!, [], cancellationToken);
            }

            results = await ReadRoundAsync(context, round, cancellationToken);
            if (results.Count < ReviewRequest.BothAxes.Count)
            {
                return await NeedsAttentionAsync(
                    spec, ParentReviewOutcome.Failed, $"The parent-spec review of {context.Head} completed without a persisted result for every axis.", [], cancellationToken);
            }
        }

        SourcedFinding[] sourced = results
            .OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value.Report.Findings.Select(finding => new SourcedFinding(StepKind.ParentReview, pair.Value.StepRunId, finding)))
            .ToArray();
        if (sourced.Length == 0)
        {
            _journal.Move(spec, SpecRunStatus.Testing);
            return await SaveAsync(cancellationToken) ? new ParentReviewResult(ParentReviewOutcome.ReadyForTesting, []) : ParentReviewResult.ConcurrencyConflict;
        }

        return await IssueFindingsAsync(context, sourced, cancellationToken);
    }

    private async Task<ParentReviewContext?> LoadContextAsync(SpecRun spec, CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(spec.RepositoryId, cancellationToken);
        if (repository is null || spec.IntegrationBaseSha is not { } integrationBase)
        {
            return null;
        }

        GitRepositoryLocation location = GitRepositoryLocation.From(repository);
        CommitSha? head = await git.GetBranchTipAsync(location, spec.IntegrationBranch, GitRefScope.Local, cancellationToken) ?? spec.IntegrationTipSha;
        return head is { } tip
            ? new ParentReviewContext(spec, await settings.GetAsync(spec.RepositoryId, cancellationToken), location, integrationBase, tip)
            : null;
    }

    private async Task<IReadOnlyDictionary<FindingAxis, ParentReviewAxisResult>> ReadRoundAsync(
        ParentReviewContext context, ReviewRound round, CancellationToken cancellationToken) =>
        ParentReviewRounds.Read(await stepRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken), round, context.Head);

    private async Task<ReviewRoundResult> ReviewAsync(ParentReviewContext context, ReviewRound round, FindingAxis[] axes, CancellationToken cancellationToken)
    {
        ParentReviewWorkspace workspace = ParentReviewWorkspace.For(context.Settings.WorkspaceRootDirectory, context.Spec.Id);
        await git.PrepareWorktreeAsync(context.Location, new WorktreeSpec(workspace.Branch, context.Head, workspace.CheckoutDirectory), cancellationToken);
        try
        {
            var request = new ReviewRequest(
                context.Spec.Id,
                null,
                ReviewScope.ParentSpec,
                new ReviewTarget(workspace.CheckoutDirectory, workspace.Branch, context.Base, context.Head),
                round,
                axes);
            return await reviews.RunAsync(request, cancellationToken);
        }
        finally
        {
            // The checkout is recreated from the integration tip for every round, so it is removed even when the run is cancelled.
            await git.CleanupWorktreeAsync(context.Location, workspace.CheckoutDirectory, CancellationToken.None);
        }
    }

    private async Task<ParentReviewResult> IssueFindingsAsync(ParentReviewContext context, SourcedFinding[] sourced, CancellationToken cancellationToken)
    {
        SpecRun spec = context.Spec;
        FindingIssuanceResult issuance = await findings.IssueAsync(spec, sourced, cancellationToken);
        if (issuance.Outcome != FindingIssuanceOutcome.Issued)
        {
            return ParentReviewResult.ConcurrencyConflict;
        }

        Dictionary<TicketRunId, TicketRun> tickets = (await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken)).ToDictionary(ticket => ticket.Id);
        string issueList = string.Join(", ", issuance.Tickets.Select(ticket => $"#{ticket.Issue.Number}"));
        if (issuance.Tickets.All(ticket => tickets[ticket.TicketRunId].IsTerminal))
        {
            return await NeedsAttentionAsync(
                spec,
                ParentReviewOutcome.NoNewWork,
                $"Parent-spec review cycle {spec.ReviewCycle} only repeated findings whose tickets are already done ({issueList}); working the tickets again cannot resolve them.",
                issuance.Tickets,
                cancellationToken);
        }

        int limit = context.Settings.ParentReviewCycleLimit;
        if (spec.ReviewCycle >= limit)
        {
            return await NeedsAttentionAsync(
                spec,
                ParentReviewOutcome.CycleLimitReached,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The parent-spec review still found {sourced.Length} issue(s) after {spec.ReviewCycle} parent-spec review cycle(s); the limit is {limit}. Finding tickets: {issueList}."),
                issuance.Tickets,
                cancellationToken);
        }

        _journal.Move(spec, SpecRunStatus.Running);
        return await SaveAsync(cancellationToken)
            ? new ParentReviewResult(ParentReviewOutcome.FindingTicketsCreated, issuance.Tickets)
            : ParentReviewResult.ConcurrencyConflict;
    }

    private async Task<ParentReviewResult> NeedsAttentionAsync(
        SpecRun spec, ParentReviewOutcome outcome, string reason, IReadOnlyList<FindingTicket> tickets, CancellationToken cancellationToken)
    {
        _journal.MarkNeedsAttention(spec, reason);
        return await SaveAsync(cancellationToken) ? new ParentReviewResult(outcome, tickets, reason) : ParentReviewResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    /// <param name="Base">Where the spec's integration branch started; the review covers everything the spec introduced since.</param>
    /// <param name="Head">The reviewed integration tip.</param>
    private sealed record ParentReviewContext(SpecRun Spec, EffectiveSettings Settings, GitRepositoryLocation Location, CommitSha Base, CommitSha Head);
}
