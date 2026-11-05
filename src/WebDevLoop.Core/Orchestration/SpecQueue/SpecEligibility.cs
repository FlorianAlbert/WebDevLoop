using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Current opt-in and open-work requirements shared by discovery, enqueue and scheduling.</summary>
public static class SpecEligibility
{
    public static async Task<(IssueSnapshot Spec, string? Rejection)> ReadAsync(
        IGitHubIssues issues, IssueRef parent, CancellationToken cancellationToken)
    {
        IssueSnapshot spec = await issues.GetIssueAsync(parent, cancellationToken);
        if (RejectionOf(spec) is { } rejection)
        {
            return (spec, rejection);
        }

        IReadOnlyList<IssueSnapshot> tickets = await issues.GetSubIssuesAsync(spec.Ref, cancellationToken);
        return tickets.Any(ticket => ticket.IsOpenAndOptedIn())
            ? (spec, null)
            : (spec, $"Parent spec #{parent.Number} needs at least one open sub-issue independently labeled '{WebDevLoopOptIn.Label}'.");
    }

    /// <summary>Why the parent snapshot itself (state and WebDevLoop Opt-in) disqualifies the spec, or null.</summary>
    public static string? RejectionOf(IssueSnapshot spec)
    {
        if (spec.State != IssueState.Open)
        {
            return $"Parent spec #{spec.Ref.Number} must be open.";
        }

        return spec.HasOptIn()
            ? null
            : $"Parent spec #{spec.Ref.Number} requires the '{WebDevLoopOptIn.Label}' label (WebDevLoop Opt-in).";
    }
}
