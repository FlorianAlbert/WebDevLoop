using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Findings;

public enum FindingIssuanceOutcome
{
    /// <summary>Every finding has an issue and a ticket run; see <see cref="FindingIssuanceResult.Tickets"/>.</summary>
    Issued,

    /// <summary>A save lost a compare-and-swap race (e.g. a duplicate issuer planned the same fingerprint); nothing more was done.</summary>
    ConcurrencyConflict,
}

public enum FindingTicketOrigin
{
    /// <summary>The ticket run was created for this finding now (its issue may have been recovered from an earlier attempt).</summary>
    NewTicket,

    /// <summary>The finding was issued before (e.g. reported again in a later cycle); its existing ticket run is reused.</summary>
    ExistingTicket,
}

/// <summary>The finding ticket (sub-issue of the spec plus ticket run) for one fingerprint.</summary>
public sealed record FindingTicket(FindingFingerprint Fingerprint, IssueRef Issue, TicketRunId TicketRunId, FindingTicketOrigin Origin);

/// <param name="Tickets">One ticket per distinct fingerprint, in the order the findings were given.</param>
/// <param name="AddedDependencies">Blocking relations added to GitHub and the ticket DAG.</param>
/// <param name="RejectedDependencies">Planned blocking relations that would have closed a dependency cycle; not added.</param>
public sealed record FindingIssuanceResult(
    FindingIssuanceOutcome Outcome,
    IReadOnlyList<FindingTicket> Tickets,
    IReadOnlyList<DependencyEdge<TicketRunId>> AddedDependencies,
    IReadOnlyList<DependencyEdge<TicketRunId>> RejectedDependencies)
{
    public static FindingIssuanceResult ConcurrencyConflict { get; } = new(FindingIssuanceOutcome.ConcurrencyConflict, [], [], []);
}
