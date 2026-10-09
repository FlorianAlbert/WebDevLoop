using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>
/// Workflow step 1: snapshots the spec's open sub-issues as <see cref="TicketRun"/>s and their native blocking edges as
/// <see cref="TicketDependency"/>s. Closed sub-issues are already done and edges to issues outside the spec are not part of
/// the ticket DAG. Idempotent per run: an existing snapshot is kept.
/// </summary>
public sealed class SpecSnapshotter(IGitHubIssues issues, ITicketRunRepository ticketRuns, IIdGenerator ids, IClock clock)
{
    /// <returns>Null when the snapshot exists afterwards; otherwise why it was rejected (nothing is added).</returns>
    public async Task<string?> SnapshotAsync(SpecRun run, CancellationToken cancellationToken)
    {
        if ((await ticketRuns.ListBySpecRunAsync(run.Id, cancellationToken)).Count > 0)
        {
            return null;
        }

        SpecIssueGraph graph = await issues.GetSpecGraphAsync(run.ParentIssue, cancellationToken);
        IssueSnapshot[] openTickets = graph.Tickets.Where(ticket => ticket.State == IssueState.Open).ToArray();
        DependencyEdge<IssueRef>[] edges = graph.TicketDependencies
            .Select(edge => (Blocked: Find(openTickets, edge.Blocked), Blocking: Find(openTickets, edge.Blocking)))
            .Where(edge => edge.Blocked is not null && edge.Blocking is not null)
            .Select(edge => new DependencyEdge<IssueRef>(edge.Blocked!.Ref, edge.Blocking!.Ref))
            .Distinct()
            .ToArray();

        try
        {
            DependencyGraph.EnsureAcyclic(edges);
        }
        catch (DependencyCycleException exception)
        {
            return $"The ticket dependency graph of spec {run.ParentIssue} contains a cycle: {exception.Message}";
        }

        DateTimeOffset now = clock.UtcNow;
        Dictionary<IssueRef, TicketRun> byIssue = openTickets.ToDictionary(
            ticket => ticket.Ref,
            ticket => TicketRun.Create(ids.NewTicketRunId(), run.Id, ticket.Ref, ticket.Title, ticket.Body, now));
        foreach (TicketRun ticket in byIssue.Values)
        {
            ticketRuns.Add(ticket);
        }

        foreach (DependencyEdge<IssueRef> edge in edges)
        {
            ticketRuns.AddDependency(TicketDependency.Create(run.Id, byIssue[edge.Blocked].Id, byIssue[edge.Blocking].Id, DependencySource.GitHub));
        }

        return null;
    }

    private static IssueSnapshot? Find(IEnumerable<IssueSnapshot> tickets, IssueRef issue) =>
        tickets.FirstOrDefault(ticket => SpecIssues.AreSame(ticket.Ref, issue));
}
