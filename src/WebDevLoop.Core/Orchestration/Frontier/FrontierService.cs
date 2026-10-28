using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Completion;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Orchestration.WorkSelection;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <summary>
/// Recomputes a running spec's frontier using current independent opt-in and native blockers, then checks completion.
/// Selected tickets may finish after opt-out. Dispatchable tickets are claimed while other implementers keep running. Idempotent,
/// so duplicate events, periodic reconciliation, and recovery may all call it.
/// </summary>
public sealed class FrontierService(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    CurrentWorkEligibility eligibility,
    TicketDispatcher dispatcher,
    WorkCompletionDecider completion,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    RepositoryOptIn optIn)
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

        var repository = new GitHubRepoRef(specRun.ParentIssue.Owner, specRun.ParentIssue.Repo);
        RepositoryLabelCheck label = await optIn.CheckAsync(repository, cancellationToken);
        if (label.ToDeferredResult(
            new FrontierResult(FrontierOutcome.RepositoryLabelMissing, [], []),
            error => new FrontierResult(FrontierOutcome.GitHubUnavailable, [], []) { LookupErrors = [error] }) is { } deferred)
        {
            return deferred;
        }

        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken);
        CurrentWork current = await eligibility.ReadAsync(specRun, tickets, cancellationToken);
        var unblockedTickets = new HashSet<TicketRunId>(current.Runnable);
        unblockedTickets.UnionWith(tickets
            .Where(ticket => ticket.IsSelectedAndOutstanding && (ticket.Status is TicketRunStatus.Ready or TicketRunStatus.Blocked))
            .Select(ticket => ticket.Id));
        IReadOnlyList<GitHubIssuesException> lookupErrors = current.Errors;
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
        bool conflict = dispatch.ConcurrencyConflict;
        if (!conflict)
        {
            WorkCompletionResult decision = await completion.DecideCompletionAsync(specRunId, current, cancellationToken);
            conflict = decision.Outcome == WorkCompletionOutcome.ConcurrencyConflict;
            if (decision.Outcome == WorkCompletionOutcome.GitHubUnavailable && decision.LookupError is { } error)
            {
                lookupErrors = [.. lookupErrors, error];
            }
        }

        FrontierOutcome outcome = conflict
            ? FrontierOutcome.ConcurrencyConflict
            : lookupErrors.Count > 0 ? FrontierOutcome.GitHubUnavailable : FrontierOutcome.Reconciled;
        return new FrontierResult(outcome, snapshot.Unblocked, dispatch.Dispatched) { LookupErrors = lookupErrors };
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
