using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Findings;

/// <summary>
/// Workflow steps 10 and 12: turns structured parent-review or tester findings into finding tickets — sub-issues of the
/// spec plus <see cref="TicketRun"/>s that the normal ticket flow picks up — with blocking relations where the findings
/// indicate them (see <see cref="FindingDependencyPlanner"/>). Idempotent per normalized fingerprint: an issuance record
/// is saved before the issue is created and completed together with the ticket run afterwards, so a restart in between
/// inspects the spec's sub-issues for the embedded fingerprint instead of creating the issue again, and a finding reported
/// again later reuses its ticket. Blocking relations that would close a dependency cycle are rejected.
/// </summary>
public sealed class FindingTicketIssuer(
    ITicketRunRepository ticketRuns,
    IFindingIssuanceRepository issuances,
    IGitHubIssues issues,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock)
{
    public async Task<FindingIssuanceResult> IssueAsync(SpecRun spec, IReadOnlyList<SourcedFinding> findings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(findings);
        PlannedFinding[] planned = findings
            .Select(finding => new PlannedFinding(FindingFingerprints.Compute(spec.Id, finding.SourceKind, finding.Finding), finding))
            .DistinctBy(finding => finding.Fingerprint)
            .ToArray();
        if (await PlanAsync(spec, planned, cancellationToken) is not { } newlyPlanned)
        {
            return FindingIssuanceResult.ConcurrencyConflict;
        }

        var tickets = new List<FindingTicket>(planned.Length);
        foreach (PlannedFinding finding in planned)
        {
            if (await IssueAsync(spec, finding, recovering: !newlyPlanned.Contains(finding.Fingerprint), cancellationToken) is not { } ticket)
            {
                return FindingIssuanceResult.ConcurrencyConflict;
            }

            tickets.Add(ticket);
        }

        return await LinkAsync(spec, planned, tickets, cancellationToken);
    }

    /// <summary>Saves the pre-creation issuance record of every finding not issued before.</summary>
    /// <returns>The newly planned fingerprints, or null when another issuer planned one of them first.</returns>
    private async Task<IReadOnlySet<FindingFingerprint>?> PlanAsync(SpecRun spec, PlannedFinding[] planned, CancellationToken cancellationToken)
    {
        var added = new HashSet<FindingFingerprint>();
        foreach (PlannedFinding finding in planned)
        {
            if (await issuances.FindAsync(spec.Id, finding.Fingerprint, cancellationToken) is null)
            {
                SourcedFinding source = finding.Source;
                issuances.Add(FindingIssuance.Plan(spec.Id, source.SourceStepRunId, source.Finding.Axis, finding.Fingerprint, clock.UtcNow));
                added.Add(finding.Fingerprint);
            }
        }

        return added.Count == 0 || await SaveAsync(cancellationToken) ? added : null;
    }

    /// <param name="recovering">The issuance was planned by an earlier, interrupted attempt that may have created the issue.</param>
    /// <returns>Null when the post-creation save lost a race.</returns>
    private async Task<FindingTicket?> IssueAsync(SpecRun spec, PlannedFinding finding, bool recovering, CancellationToken cancellationToken)
    {
        FindingIssuance issuance = await issuances.FindAsync(spec.Id, finding.Fingerprint, cancellationToken)
            ?? throw new InvalidOperationException($"Finding '{finding.Fingerprint}' of spec run '{spec.Id}' has no issuance record.");
        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken);
        if (issuance is { Status: FindingIssuanceStatus.Created, IssueNumber: { } number }
            && TicketFor(tickets, IssueOf(spec, number)) is { } existing)
        {
            return new FindingTicket(finding.Fingerprint, existing.Issue, existing.Id, FindingTicketOrigin.ExistingTicket);
        }

        IssueSnapshot issue = issuance is { Status: FindingIssuanceStatus.Created, IssueNumber: { } createdNumber }
            ? await issues.GetIssueAsync(IssueOf(spec, createdNumber), cancellationToken)
            : await CreateIssueAsync(spec, finding, recovering, cancellationToken);
        long databaseId = issue.Ref.DatabaseId
            ?? throw new InvalidOperationException($"GitHub returned finding issue {issue.Ref} without its database id.");
        issuance.RecordCreated(issue.Ref.Number, databaseId, clock.UtcNow);
        TicketRun? ticket = TicketFor(tickets, issue.Ref);
        if (ticket is null)
        {
            ticket = TicketRun.Create(ids.NewTicketRunId(), spec.Id, issue.Ref, issue.Title, issue.Body, clock.UtcNow);
            ticketRuns.Add(ticket);
        }

        return await SaveAsync(cancellationToken)
            ? new FindingTicket(finding.Fingerprint, ticket.Issue, ticket.Id, FindingTicketOrigin.NewTicket)
            : null;
    }

    private async Task<IssueSnapshot> CreateIssueAsync(SpecRun spec, PlannedFinding finding, bool recovering, CancellationToken cancellationToken)
    {
        IssueSnapshot? issue = recovering ? await issues.FindFindingIssueAsync(spec.ParentIssue, finding.Fingerprint, cancellationToken) : null;
        issue ??= await issues.CreateFindingIssueAsync(FindingIssueDrafts.Create(spec, finding.Source, finding.Fingerprint), cancellationToken);
        await issues.AddSubIssueAsync(spec.ParentIssue, issue.Ref, cancellationToken);
        return issue;
    }

    private async Task<FindingIssuanceResult> LinkAsync(SpecRun spec, PlannedFinding[] planned, List<FindingTicket> tickets, CancellationToken cancellationToken)
    {
        Dictionary<FindingFingerprint, FindingTicket> byFingerprint = tickets.ToDictionary(ticket => ticket.Fingerprint);
        Dictionary<TicketRunId, TicketRun> runs = (await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken)).ToDictionary(ticket => ticket.Id);
        List<DependencyEdge<TicketRunId>> edges = (await ticketRuns.ListDependenciesAsync(spec.Id, cancellationToken)).Select(edge => edge.ToEdge()).ToList();
        var added = new List<DependencyEdge<TicketRunId>>();
        var rejected = new List<DependencyEdge<TicketRunId>>();
        foreach (DependencyEdge<FindingFingerprint> relation in FindingDependencyPlanner.Plan(planned.Select(finding => (finding.Fingerprint, finding.Source.Finding)).ToArray()))
        {
            (FindingTicket blocked, FindingTicket blocking) = (byFingerprint[relation.Blocked], byFingerprint[relation.Blocking]);
            var candidate = new DependencyEdge<TicketRunId>(blocked.TicketRunId, blocking.TicketRunId);
            if (runs[blocked.TicketRunId].IsTerminal || edges.Contains(candidate))
            {
                continue;
            }

            try
            {
                DependencyGraph.EnsureCanAdd(edges, candidate);
            }
            catch (DependencyCycleException)
            {
                rejected.Add(candidate);
                continue;
            }

            await issues.AddBlockedByAsync(blocked.Issue, blocking.Issue, cancellationToken);
            ticketRuns.AddDependency(TicketDependency.Create(spec.Id, blocked.TicketRunId, blocking.TicketRunId, DependencySource.CreatedFinding));
            edges.Add(candidate);
            added.Add(candidate);
        }

        return added.Count == 0 || await SaveAsync(cancellationToken)
            ? new FindingIssuanceResult(FindingIssuanceOutcome.Issued, tickets, added, rejected)
            : FindingIssuanceResult.ConcurrencyConflict;
    }

    private static IssueRef IssueOf(SpecRun spec, int number) => new(spec.ParentIssue.Owner, spec.ParentIssue.Repo, number);

    private static TicketRun? TicketFor(IEnumerable<TicketRun> tickets, IssueRef issue) =>
        tickets.FirstOrDefault(ticket => SpecIssues.AreSame(ticket.Issue, issue));

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    private sealed record PlannedFinding(FindingFingerprint Fingerprint, SourcedFinding Source);
}
