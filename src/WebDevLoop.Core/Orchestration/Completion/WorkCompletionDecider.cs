using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.WorkSelection;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion;

/// <summary>
/// Decides from current permission and remaining work whether to defer, park, finish without further work, or start scoped parent review.
/// Selected tickets finish before opt-out takes effect; stopped and empty runs retain existing branches and pull requests.
/// </summary>
public sealed class WorkCompletionDecider(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    CurrentWorkEligibility eligibility,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    RepositoryOptIn optIn)
{
    private readonly SpecRunJournal _journal = new(outbox, clock);

    /// <summary>Reads fresh GitHub eligibility for an independent completion event.</summary>
    public Task<WorkCompletionResult> DecideCompletionAsync(RunId specRunId, CancellationToken cancellationToken) =>
        DecideCompletionAsync(specRunId, null, cancellationToken);

    /// <summary>Uses the frontier pass's eligibility snapshot, with current persisted ticket state after dispatch; decisions that change status or reason re-read eligibility after the label check.</summary>
    internal async Task<WorkCompletionResult> DecideCompletionAsync(RunId specRunId, CurrentWork? current, CancellationToken cancellationToken)
    {
        SpecRun? spec = await specRuns.GetAsync(specRunId, cancellationToken);
        if (spec is not { Status: SpecRunStatus.Running })
        {
            return new(WorkCompletionOutcome.NotRunning);
        }

        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken);
        current ??= await eligibility.ReadAsync(spec, tickets, cancellationToken);
        CompletionDecision decision = Decide(current, tickets);
        if (ChangesPersistedState(spec, decision))
        {
            // Deleting the repository label also strips it from issues, so a missing label must not be read as parent opt-out.
            // The label is verified before the eligibility read that decides (it may have been recreated and issues re-opted in
            // since an earlier read) and again after it (it may have been deleted in between).
            if (await DeferWhileLabelUnavailableAsync(spec, current, cancellationToken) is { } labelDeferred)
            {
                return labelDeferred;
            }

            if (decision.Outcome is not WorkCompletionOutcome.Deferred)
            {
                current = await eligibility.ReadAsync(spec, tickets, cancellationToken);
                decision = Decide(current, tickets);
                if (ChangesPersistedState(spec, decision) && await DeferWhileLabelUnavailableAsync(spec, current, cancellationToken) is { } deleted)
                {
                    return deleted;
                }
            }
        }

        if (!ChangesPersistedState(spec, decision))
        {
            return new(decision.Outcome, current.Errors.FirstOrDefault());
        }

        if (decision.NextStatus is { } next)
        {
            _journal.Move(spec, next);
        }

        if (spec.Status is SpecRunStatus.Running or SpecRunStatus.WaitingForTickets or SpecRunStatus.Completed)
        {
            spec.SetWorkDecisionReason(decision.Reason);
        }
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? new(decision.Outcome, current.Errors.FirstOrDefault())
            : new(WorkCompletionOutcome.ConcurrencyConflict);
    }

    private static bool ChangesPersistedState(SpecRun spec, CompletionDecision decision) =>
        decision.NextStatus is not null || spec.WorkDecisionReason != decision.Reason;

    private async Task<WorkCompletionResult?> DeferWhileLabelUnavailableAsync(SpecRun spec, CurrentWork current, CancellationToken cancellationToken)
    {
        var repository = new GitHubRepoRef(spec.ParentIssue.Owner, spec.ParentIssue.Repo);
        RepositoryLabelCheck label = await optIn.CheckAsync(repository, cancellationToken);
        return label.ToDeferredResult(
            new WorkCompletionResult(WorkCompletionOutcome.RepositoryLabelMissing, current.Errors.FirstOrDefault()),
            error => new WorkCompletionResult(WorkCompletionOutcome.GitHubUnavailable, error));
    }

    private static CompletionDecision Decide(CurrentWork current, IReadOnlyList<TicketRun> tickets)
    {
        bool selectedOutstanding = tickets.Any(ticket => ticket.IsSelectedAndOutstanding);
        if (current.Errors.Count == 0 && !selectedOutstanding && current.AllEligibleBlocked)
        {
            return new(WorkCompletionOutcome.Parked, CurrentWork.BlockedReason, SpecRunStatus.WaitingForTickets);
        }

        bool parentBlockersDeferProgress = current.WaitReason is not null && current.HasWork(tickets);
        if (current.Errors.Count > 0 || parentBlockersDeferProgress)
        {
            return new(WorkCompletionOutcome.Deferred, current.Errors.FirstOrDefault()?.Message ?? current.WaitReason);
        }

        if (selectedOutstanding)
        {
            return new(WorkCompletionOutcome.TicketsOutstanding, current.StopReason);
        }

        if (current.GetTerminalReason(tickets) is { } terminalReason)
        {
            return new(
                current.StopReason is not null ? WorkCompletionOutcome.Stopped : WorkCompletionOutcome.NoEligibleWork,
                terminalReason,
                SpecRunStatus.Completed);
        }

        if (current.Eligible.Count > 0)
        {
            return new(WorkCompletionOutcome.TicketsOutstanding, current.Runnable.Count == 0 ? BlockedWorkReason : null);
        }

        return new(WorkCompletionOutcome.ReviewStarted, null, SpecRunStatus.ParentReviewing);
    }

    private const string BlockedWorkReason = "Eligible tickets remain blocked or await graph reconciliation; parent review is deferred.";

    private readonly record struct CompletionDecision(WorkCompletionOutcome Outcome, string? Reason, SpecRunStatus? NextStatus = null);
}
