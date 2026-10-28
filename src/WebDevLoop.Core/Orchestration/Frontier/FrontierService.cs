using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <summary>
/// Recomputes a running spec's ticket frontier (workflow step 8): Blocked tickets whose native GitHub blockers are all closed
/// become Ready, and dispatchable tickets are claimed and launched while other implementers keep running. Idempotent,
/// so duplicate events, periodic reconciliation, and recovery may all call it.
/// </summary>
public sealed class FrontierService(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IGitHubIssues issues,
    TicketDispatcher dispatcher,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>A lost race is recomputed from fresh state right away instead of waiting for periodic reconciliation.</summary>
    private const int MaxAttempts = 3;

    public async Task<FrontierResult> ReconcileAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        var unblocked = new List<TicketRunId>();
        var dispatched = new List<TicketRunId>();
        var lookupErrors = new List<GitHubIssuesException>();
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            FrontierResult pass = await ReconcileOnceAsync(specRunId, cancellationToken);
            unblocked.AddRange(pass.Unblocked);
            dispatched.AddRange(pass.Dispatched);
            lookupErrors.AddRange(pass.LookupErrors);
            if (pass.Outcome != FrontierOutcome.ConcurrencyConflict)
            {
                return pass with
                {
                    Outcome = lookupErrors.Count > 0 ? FrontierOutcome.GitHubUnavailable : pass.Outcome,
                    Unblocked = unblocked,
                    Dispatched = dispatched,
                    LookupErrors = lookupErrors,
                };
            }
        }

        return new FrontierResult(FrontierOutcome.ConcurrencyConflict, unblocked, dispatched) { LookupErrors = lookupErrors };
    }

    /// <summary>Reconciles every running spec, e.g. after an implementer slot was freed somewhere.</summary>
    public async Task<IReadOnlyList<FrontierResult>> ReconcileAllRunningAsync(CancellationToken cancellationToken)
    {
        RunId[] running = (await specRuns.ListNonTerminalAsync(cancellationToken))
            .Where(run => run.Status == SpecRunStatus.Running)
            .OrderBy(run => run.RepositoryId)
            .ThenBy(run => run.QueuePosition)
            .Select(run => run.Id)
            .ToArray();
        var results = new List<FrontierResult>(running.Length);
        foreach (RunId specRunId in running)
        {
            results.Add(await ReconcileAsync(specRunId, cancellationToken));
        }

        return results;
    }

    private async Task<FrontierResult> ReconcileOnceAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        SpecRun? specRun = await specRuns.GetAsync(specRunId, cancellationToken);
        if (specRun is not { Status: SpecRunStatus.Running })
        {
            return FrontierResult.NotRunning;
        }

        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken);
        (HashSet<TicketRunId> unblockedTickets, IReadOnlyList<GitHubIssuesException> lookupErrors) = await CheckBlockersAsync(tickets, cancellationToken);
        FrontierSnapshot snapshot = TicketFrontier.Compute(tickets, unblockedTickets);
        Dictionary<TicketRunId, TicketRun> byId = tickets.ToDictionary(ticket => ticket.Id);
        TicketRun[] reblocked = tickets.Where(ticket => ticket.Status == TicketRunStatus.Ready && !unblockedTickets.Contains(ticket.Id)).ToArray();

        if (snapshot.Unblocked.Count > 0 || reblocked.Length > 0)
        {
            UpdateReadiness(specRunId, reblocked, snapshot.Unblocked.Select(ticketId => byId[ticketId]));
            if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
            {
                return new FrontierResult(FrontierOutcome.ConcurrencyConflict, [], []) { LookupErrors = lookupErrors };
            }
        }

        TicketRun[] candidates = snapshot.Dispatchable.Select(ticketId => byId[ticketId]).ToArray();
        DispatchResult dispatch = await dispatcher.DispatchAsync(specRun, candidates, cancellationToken);
        FrontierOutcome outcome = dispatch.ConcurrencyConflict
            ? FrontierOutcome.ConcurrencyConflict
            : lookupErrors.Count > 0 ? FrontierOutcome.GitHubUnavailable : FrontierOutcome.Reconciled;
        return new FrontierResult(outcome, snapshot.Unblocked, dispatch.Dispatched) { LookupErrors = lookupErrors };
    }

    private async Task<(HashSet<TicketRunId> Unblocked, IReadOnlyList<GitHubIssuesException> Errors)> CheckBlockersAsync(
        IReadOnlyList<TicketRun> tickets, CancellationToken cancellationToken)
    {
        var unblocked = new HashSet<TicketRunId>();
        var errors = new List<GitHubIssuesException>();
        foreach (TicketRun ticket in tickets.Where(ticket => ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready))
        {
            try
            {
                if (await IsUnblockedAsync(ticket, cancellationToken))
                {
                    unblocked.Add(ticket.Id);
                }
            }
            catch (GitHubIssuesException exception)
            {
                errors.Add(new GitHubIssuesException($"Current GitHub blocker check for ticket {ticket.Issue} failed; dispatch deferred for recovery. {exception.Message}", exception));
            }
        }

        return (unblocked, errors);
    }

    private async Task<bool> IsUnblockedAsync(TicketRun ticket, CancellationToken cancellationToken)
    {
        IssueSnapshot current = await issues.GetIssueAsync(ticket.Issue, cancellationToken);
        if (current.State != IssueState.Open)
        {
            return false;
        }

        foreach (IssueRef blocker in current.BlockedBy)
        {
            IssueSnapshot blocking = await issues.GetIssueAsync(blocker, cancellationToken);
            if (blocking.State != IssueState.Closed)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateReadiness(RunId specRunId, IEnumerable<TicketRun> reblocked, IEnumerable<TicketRun> unblocked)
    {
        DateTimeOffset now = clock.UtcNow;
        foreach (TicketRun ticket in reblocked)
        {
            ticket.TransitionTo(TicketRunStatus.Blocked, now);
            outbox.Append(new TicketRunStatusChanged(specRunId, ticket.Id, TicketRunStatus.Ready, TicketRunStatus.Blocked, now));
        }

        foreach (TicketRun ticket in unblocked)
        {
            ticket.TransitionTo(TicketRunStatus.Ready, now);
            outbox.Append(new TicketRunStatusChanged(specRunId, ticket.Id, TicketRunStatus.Blocked, TicketRunStatus.Ready, now));
        }
    }
}
