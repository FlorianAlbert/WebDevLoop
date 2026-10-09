using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Snapshot of a parent spec issue, its sub-issues (tickets), and the blocking edges between those tickets.</summary>
/// <param name="Spec">The parent spec; <see cref="IssueSnapshot.BlockedBy"/> holds spec-level dependencies.</param>
public sealed record SpecIssueGraph(
    IssueSnapshot Spec,
    IReadOnlyList<IssueSnapshot> Tickets,
    IReadOnlyList<DependencyEdge<IssueRef>> TicketDependencies);
