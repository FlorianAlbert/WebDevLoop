using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Workflow steps 5–6 for one ticket in <c>Reviewing</c> (entry point for <see cref="IReviewLoopLauncher"/>): runs both
/// independent review axes against the implementer's branch; if either finds issues, the original implementer fixes them
/// and both axes review the updated branch again, until both are clean (→ <c>Integrating</c>) or <c>MaxReviewIterations</c>
/// rounds are used up (→ <c>NeedsAttention</c>). Round results are persisted with the review steps, so the loop resumes
/// where it stopped, e.g. after waiting for an implementer slot for the fix turn.
/// </summary>
public sealed class TicketReviewLoop(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IEffectiveSettingsProvider settings,
    IGitWorkspace git,
    TwoAxisReviewRunner reviews,
    ReviewFixRunner fixes,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private readonly ReviewLoopJournal _journal = new(stepRuns, outbox, clock);

    public async Task<ReviewLoopResult> RunAsync(ReviewAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TicketRun? ticket = await ticketRuns.GetAsync(assignment.TicketRunId, cancellationToken);
            if (ticket is not { Status: TicketRunStatus.Reviewing })
            {
                return ReviewLoopResult.NotReviewing;
            }

            IReadOnlyList<StepRun> steps = await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken);
            if (steps.Any(step => step.IsActive))
            {
                return ReviewLoopResult.AlreadyRunning;
            }

            if (await LoadContextAsync(ticket, cancellationToken) is not { } context || ticket.LastImplementedSha is not { } head)
            {
                return await NeedsAttentionAsync(ticket, ReviewLoopOutcome.Failed, $"Spec run, repository, or implemented commit of ticket '{ticket.Id}' is missing.", cancellationToken);
            }

            var reports = new Dictionary<FindingAxis, ReviewReport>(ReviewStepResults.ReadCurrentRound(ticket, steps));
            FindingAxis[] missing = ReviewRequest.BothAxes.Where(axis => !reports.ContainsKey(axis)).ToArray();
            if (missing.Length > 0)
            {
                ReviewRoundResult round = await ReviewAsync(context, head, missing, cancellationToken);
                if (round.Outcome != ReviewRoundOutcome.Completed)
                {
                    return await StopAfterReviewAsync(ticket, round, cancellationToken);
                }

                foreach (ReviewReport report in round.Reports)
                {
                    reports[report.Axis] = report;
                }
            }

            if (await DecideAsync(context, reports.Values.SelectMany(report => report.Findings).ToArray(), cancellationToken) is { } final)
            {
                return final;
            }
        }
    }

    private async Task<ImplementationContext?> LoadContextAsync(TicketRun ticket, CancellationToken cancellationToken)
    {
        SpecRun? spec = await specRuns.GetAsync(ticket.SpecRunId, cancellationToken);
        RepositoryRecord? repository = spec is null ? null : await repositories.GetAsync(spec.RepositoryId, cancellationToken);
        if (spec is null || repository is null)
        {
            return null;
        }

        EffectiveSettings effective = await settings.GetAsync(spec.RepositoryId, cancellationToken);
        return new ImplementationContext(
            spec,
            ticket,
            repository,
            effective,
            GitRepositoryLocation.From(repository),
            RunWorkspaceLayout.For(effective.WorkspaceRootDirectory, spec.Id));
    }

    private async Task<ReviewRoundResult> ReviewAsync(ImplementationContext context, CommitSha head, FindingAxis[] axes, CancellationToken cancellationToken)
    {
        if (await DiffBaseAsync(context, head, cancellationToken) is not { } diffBase)
        {
            return ReviewRoundResult.Failed($"Ticket branch '{context.Ticket.BranchName}' at {head} is not based on the integration branch.");
        }

        TicketRun ticket = context.Ticket;
        var request = new ReviewRequest(
            context.Spec.Id,
            ticket.Id,
            ReviewScope.Ticket,
            new ReviewTarget(context.WorktreePath, ticket.BranchName, diffBase, head),
            ReviewStepResults.CurrentRound(ticket),
            axes);
        return await reviews.RunAsync(request, cancellationToken);
    }

    /// <summary>
    /// The integration commit last merged into the ticket branch (the merge base with the integration tip), so the diff is
    /// exactly the ticket's own changes even when the tip has moved on since. The local integration ref is the freshest
    /// tip; the recorded tips cover a ref that is missing locally.
    /// </summary>
    private async Task<CommitSha?> DiffBaseAsync(ImplementationContext context, CommitSha head, CancellationToken cancellationToken)
    {
        SpecRun spec = context.Spec;
        CommitSha? localTip = await git.GetBranchTipAsync(context.Location, spec.IntegrationBranch, GitRefScope.Local, cancellationToken);
        foreach (CommitSha candidate in new[] { localTip, spec.IntegrationTipSha, spec.IntegrationBaseSha }.OfType<CommitSha>().Distinct())
        {
            if (await git.MergeBaseAsync(context.Location, head, candidate, cancellationToken) is { } mergeBase)
            {
                return mergeBase;
            }
        }

        return null;
    }

    private async Task<ReviewLoopResult> StopAfterReviewAsync(TicketRun ticket, ReviewRoundResult round, CancellationToken cancellationToken) =>
        round.Outcome switch
        {
            ReviewRoundOutcome.Cancelled => new ReviewLoopResult(ReviewLoopOutcome.Cancelled, round.Reason),
            ReviewRoundOutcome.ConcurrencyConflict => ReviewLoopResult.ConcurrencyConflict,
            _ => await NeedsAttentionAsync(ticket, ReviewLoopOutcome.Failed, round.Reason!, cancellationToken),
        };

    /// <returns>The final result, or null when a fix was applied and both axes must review again.</returns>
    private async Task<ReviewLoopResult?> DecideAsync(ImplementationContext context, Finding[] findings, CancellationToken cancellationToken)
    {
        TicketRun ticket = context.Ticket;
        if (findings.Length == 0)
        {
            _journal.Move(ticket, TicketRunStatus.Integrating);
            return await SaveAsync(cancellationToken) ? ReviewLoopResult.Integrating : ReviewLoopResult.ConcurrencyConflict;
        }

        int completedRounds = ticket.ReviewIteration + 1;
        int maxRounds = context.Settings.MaxReviewIterations;
        if (completedRounds >= maxRounds)
        {
            return await NeedsAttentionAsync(
                ticket,
                ReviewLoopOutcome.ReviewIterationsExhausted,
                $"Review still found {findings.Length} issue(s) after {completedRounds} review round(s); the limit is {maxRounds}.",
                cancellationToken);
        }

        FixResult fix = await fixes.RunAsync(context, findings, cancellationToken);
        return fix.Outcome switch
        {
            FixOutcome.Fixed => null,
            FixOutcome.NoImplementerCapacity => ReviewLoopResult.AwaitingImplementerCapacity,
            FixOutcome.Failed => new ReviewLoopResult(ReviewLoopOutcome.Failed, fix.Reason),
            FixOutcome.Cancelled => new ReviewLoopResult(ReviewLoopOutcome.Cancelled, fix.Reason),
            _ => ReviewLoopResult.ConcurrencyConflict,
        };
    }

    private async Task<ReviewLoopResult> NeedsAttentionAsync(TicketRun ticket, ReviewLoopOutcome outcome, string reason, CancellationToken cancellationToken)
    {
        _journal.MarkNeedsAttention(ticket, reason);
        return await SaveAsync(cancellationToken) ? new ReviewLoopResult(outcome, reason) : ReviewLoopResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
}
