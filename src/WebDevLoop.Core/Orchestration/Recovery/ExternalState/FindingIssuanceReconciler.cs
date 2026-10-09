using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>
/// Completes finding issuances that were planned before a crash while their issue was already created on GitHub: the issue
/// is found among the spec's sub-issues by the fingerprint embedded in its body, recorded on the issuance, and given its
/// ticket run if it has none, exactly as the finding issuer would on its next run. Issuances whose issue does not exist
/// stay planned; the issuer creates the issue when it runs again.
/// </summary>
public sealed class FindingIssuanceReconciler(
    IFindingIssuanceRepository issuances,
    ITicketRunRepository ticketRuns,
    IGitHubIssues issues,
    IIdGenerator ids,
    IUnitOfWork unitOfWork,
    IClock clock) : ISpecReconciliationStep
{
    public async Task<IReadOnlyList<ReconciliationAction>> ReconcileAsync(SpecReconciliationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        SpecRun spec = context.Spec;
        var actions = new List<ReconciliationAction>();
        foreach (FindingIssuance issuance in await issuances.ListBySpecRunAsync(spec.Id, cancellationToken))
        {
            if (issuance.Status == FindingIssuanceStatus.Created
                || await issues.FindFindingIssueAsync(spec.ParentIssue, issuance.Fingerprint, cancellationToken) is not { Ref.DatabaseId: { } databaseId } issue)
            {
                continue;
            }

            DateTimeOffset now = clock.UtcNow;
            issuance.RecordCreated(issue.Ref.Number, databaseId, now);
            TicketRun? ticket = (await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken)).LastOrDefault(candidate => SpecIssues.AreSame(candidate.Issue, issue.Ref));
            if (ticket is null)
            {
                ticket = TicketRun.Create(ids.NewTicketRunId(), spec.Id, issue.Ref, issue.Title, issue.Body, now);
                ticketRuns.Add(ticket);
            }

            actions.Add(new ReconciliationAction(spec.Id, ticket.Id, ReconciliationActionKind.FindingIssuanceRecovered, $"Finding '{issuance.Fingerprint}' is {issue.Ref}."));
        }

        return actions.Count == 0 || await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved ? actions : [];
    }
}
