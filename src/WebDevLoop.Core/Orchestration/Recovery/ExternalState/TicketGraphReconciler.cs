using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>
/// Re-derives a running spec's ticket graph from GitHub after its snapshot (workflow step 1), so changes made by people
/// while the run is active are honoured:
/// <list type="bullet">
/// <item>An unstarted ticket whose issue was closed or detached from the spec is skipped (with a run event saying why).</item>
/// <item>An open sub-issue without a live ticket run gets one: a new sub-issue, or an issue reopened (or re-attached) after
/// it was skipped that way. Tickets that are being implemented or are done are never replaced.</item>
/// <item>The issue of an integrated ticket that was reopened after the integration closed it is recorded once; the ticket
/// stays integrated because its commit is already on the integration branch.</item>
/// <item>Blocking edges of unstarted tickets mirror GitHub's native relations between sub-issues. A blocker that was
/// closed on GitHub and therefore skipped no longer blocks (GitHub's semantics); a ticket the user skipped while its issue
/// stays open keeps blocking. Edges that would close a cycle are not added.</item>
/// </list>
/// Any change requests a frontier reconciliation through the outbox, saved together with the change.
/// </summary>
public sealed class TicketGraphReconciler(
    IGitHubIssues issues,
    ITicketRunRepository ticketRuns,
    IIntegrationSagaRepository sagas,
    IRunEventRepository runEvents,
    IIdGenerator ids,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : ISpecReconciliationStep
{
    public async Task<IReadOnlyList<ReconciliationAction>> ReconcileAsync(SpecReconciliationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        SpecRun spec = context.Spec;
        if (spec.Status != SpecRunStatus.Running)
        {
            return [];
        }

        SpecIssueGraph graph = await issues.GetSpecGraphAsync(spec.ParentIssue, cancellationToken);
        var pass = new GraphPass(
            spec,
            graph,
            (await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken)).ToList(),
            (await runEvents.ListBySpecRunAsync(spec.Id, cancellationToken))
                .Where(runEvent => runEvent.TicketRunId is not null)
                .Select(runEvent => (runEvent.TicketRunId!.Value, runEvent.Type))
                .ToHashSet(),
            clock.UtcNow);

        SkipAbandonedTickets(pass);
        await AddMissingTicketsAsync(pass, cancellationToken);
        await MirrorDependenciesAsync(pass, cancellationToken);
        if (pass.Actions.Count == 0)
        {
            return [];
        }

        if (pass.FrontierChanged)
        {
            outbox.Append(new FrontierReconciliationRequested(spec.Id, pass.Now));
        }

        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved ? pass.Actions : [];
    }

    private void SkipAbandonedTickets(GraphPass pass)
    {
        foreach (TicketRun ticket in pass.Tickets.Where(IsUnstarted).ToArray())
        {
            IssueSnapshot? issue = pass.SubIssue(ticket.Issue);
            string? reason = issue is null ? ExternalStateRunEvents.TicketRemovedFromSpec
                : issue.State == IssueState.Closed ? ExternalStateRunEvents.TicketClosedExternally
                : null;
            if (reason is null)
            {
                continue;
            }

            TicketRunStatus previous = ticket.Status;
            ticket.TransitionTo(TicketRunStatus.Skipped, pass.Now);
            outbox.Append(new TicketRunStatusChanged(pass.Spec.Id, ticket.Id, previous, TicketRunStatus.Skipped, pass.Now));
            Record(pass, ticket, reason);
            pass.Change(ticket.Id, ReconciliationActionKind.TicketSkipped, $"{reason}: {ticket.Issue}");
        }
    }

    private async Task AddMissingTicketsAsync(GraphPass pass, CancellationToken cancellationToken)
    {
        foreach (IssueSnapshot issue in pass.Graph.Tickets.Where(issue => issue.State == IssueState.Open))
        {
            TicketRun? current = pass.Current(issue.Ref);
            if (current is null || WasSkippedExternally(pass, current))
            {
                TicketRun ticket = TicketRun.Create(ids.NewTicketRunId(), pass.Spec.Id, issue.Ref, issue.Title, issue.Body, pass.Now);
                ticketRuns.Add(ticket);
                pass.Tickets.Add(ticket);
                pass.Change(ticket.Id, ReconciliationActionKind.TicketAdded, current is null ? $"New sub-issue {issue.Ref}" : $"Reopened sub-issue {issue.Ref}");
            }
            else if (current.Status == TicketRunStatus.Integrated
                && !pass.Recorded.Contains((current.Id, ExternalStateRunEvents.IntegratedTicketReopened))
                && await sagas.FindLatestForTicketAsync(current.Id, cancellationToken) is { Checkpoint: >= IntegrationSagaCheckpoint.IssueTransitioned })
            {
                Record(pass, current, ExternalStateRunEvents.IntegratedTicketReopened);
                pass.Actions.Add(new ReconciliationAction(pass.Spec.Id, current.Id, ReconciliationActionKind.IntegratedTicketReopened, $"{issue.Ref} was reopened after its integration."));
            }
        }
    }

    private async Task MirrorDependenciesAsync(GraphPass pass, CancellationToken cancellationToken)
    {
        HashSet<DependencyEdge<TicketRunId>> desired = DesiredEdges(pass);
        HashSet<TicketRunId> unstarted = pass.Tickets.Where(IsUnstarted).Select(ticket => ticket.Id).ToHashSet();
        var remaining = new List<DependencyEdge<TicketRunId>>();
        foreach (TicketDependency dependency in await ticketRuns.ListDependenciesAsync(pass.Spec.Id, cancellationToken))
        {
            if (unstarted.Contains(dependency.BlockedTicketRunId) && !desired.Contains(dependency.ToEdge()))
            {
                ticketRuns.RemoveDependency(dependency);
                pass.Change(dependency.BlockedTicketRunId, ReconciliationActionKind.TicketDependencyRemoved, $"No longer blocked by ticket run '{dependency.BlockingTicketRunId}'.");
            }
            else
            {
                remaining.Add(dependency.ToEdge());
            }
        }

        foreach (DependencyEdge<TicketRunId> edge in desired.Where(edge => !remaining.Contains(edge)))
        {
            try
            {
                DependencyGraph.EnsureCanAdd(remaining, edge);
            }
            catch (DependencyCycleException)
            {
                continue;
            }

            ticketRuns.AddDependency(TicketDependency.Create(pass.Spec.Id, edge.Blocked, edge.Blocking, DependencySource.GitHub));
            remaining.Add(edge);
            pass.Change(edge.Blocked, ReconciliationActionKind.TicketDependencyAdded, $"Blocked by ticket run '{edge.Blocking}'.");
        }
    }

    /// <summary>GitHub's relations between sub-issues, mapped to the live ticket runs, for blocked tickets that have not started.</summary>
    private static HashSet<DependencyEdge<TicketRunId>> DesiredEdges(GraphPass pass)
    {
        var desired = new HashSet<DependencyEdge<TicketRunId>>();
        foreach (DependencyEdge<IssueRef> relation in pass.Graph.TicketDependencies)
        {
            if (pass.SubIssue(relation.Blocking) is not { } blockingIssue
                || pass.SubIssue(relation.Blocked) is null
                || pass.Current(relation.Blocked) is not { } blocked
                || pass.Current(relation.Blocking) is not { } blocking
                || blocked.Id == blocking.Id
                || !IsUnstarted(blocked)
                || (blocking.Status == TicketRunStatus.Skipped && blockingIssue.State == IssueState.Closed))
            {
                continue;
            }

            desired.Add(new DependencyEdge<TicketRunId>(blocked.Id, blocking.Id));
        }

        return desired;
    }

    private static bool IsUnstarted(TicketRun ticket) => ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready;

    private static bool WasSkippedExternally(GraphPass pass, TicketRun ticket) =>
        ticket.Status == TicketRunStatus.Skipped
        && (pass.Recorded.Contains((ticket.Id, ExternalStateRunEvents.TicketClosedExternally))
            || pass.Recorded.Contains((ticket.Id, ExternalStateRunEvents.TicketRemovedFromSpec)));

    private void Record(GraphPass pass, TicketRun ticket, string type)
    {
        runEvents.Add(RunEvent.Create(pass.Spec.Id, ticket.Id, type, JsonSerializer.Serialize(new { issue = ticket.Issue.Number }), pass.Now));
        pass.Recorded.Add((ticket.Id, type));
    }

    private sealed class GraphPass(
        SpecRun spec,
        SpecIssueGraph graph,
        List<TicketRun> tickets,
        HashSet<(TicketRunId TicketRunId, string Type)> recorded,
        DateTimeOffset now)
    {
        public SpecRun Spec { get; } = spec;

        public SpecIssueGraph Graph { get; } = graph;

        public List<TicketRun> Tickets { get; } = tickets;

        public HashSet<(TicketRunId TicketRunId, string Type)> Recorded { get; } = recorded;

        public DateTimeOffset Now { get; } = now;

        public List<ReconciliationAction> Actions { get; } = [];

        /// <summary>Whether a change can affect which tickets are ready (as opposed to a record-only finding).</summary>
        public bool FrontierChanged { get; private set; }

        public IssueSnapshot? SubIssue(IssueRef issue) => Graph.Tickets.FirstOrDefault(candidate => SpecIssues.AreSame(candidate.Ref, issue));

        /// <summary>The newest ticket run for the issue; older runs of a reopened issue stay skipped.</summary>
        public TicketRun? Current(IssueRef issue) => Tickets.LastOrDefault(ticket => SpecIssues.AreSame(ticket.Issue, issue));

        public void Change(TicketRunId ticket, ReconciliationActionKind kind, string detail)
        {
            Actions.Add(new ReconciliationAction(Spec.Id, ticket, kind, detail));
            FrontierChanged = true;
        }
    }
}
