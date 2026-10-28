using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Current opt-in and open-work requirements shared by discovery and enqueue.</summary>
public static class SpecEligibility
{
    public static async Task<(IssueSnapshot Spec, string? Rejection)> ReadAsync(
        IGitHubIssues issues, IssueRef parent, CancellationToken cancellationToken)
    {
        IssueSnapshot spec = await issues.GetIssueAsync(parent, cancellationToken);
        if (spec.State != IssueState.Open)
        {
            return (spec, $"Parent spec #{parent.Number} must be open.");
        }

        if (!spec.Labels.Contains(WebDevLoopOptIn.Label, StringComparer.OrdinalIgnoreCase))
        {
            return (spec, $"Parent spec #{parent.Number} requires the '{WebDevLoopOptIn.Label}' label (WebDevLoop Opt-in).");
        }

        IReadOnlyList<IssueSnapshot> tickets = await issues.GetSubIssuesAsync(spec.Ref, cancellationToken);
        return tickets.Any(ticket => ticket.State == IssueState.Open && ticket.Labels.Contains(WebDevLoopOptIn.Label, StringComparer.OrdinalIgnoreCase))
            ? (spec, null)
            : (spec, $"Parent spec #{parent.Number} needs at least one open sub-issue independently labeled '{WebDevLoopOptIn.Label}'.");
    }
}
