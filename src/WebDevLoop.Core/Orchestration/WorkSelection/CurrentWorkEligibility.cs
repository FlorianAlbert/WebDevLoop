using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.WorkSelection;

/// <summary>Reads permission and native blockers at work-decision points, never from a saved issue snapshot.</summary>
public sealed class CurrentWorkEligibility(IGitHubIssues issues)
{
    private readonly ParentBlockerReader _blockers = new(issues.GetIssueAsync);

    public async Task<CurrentWork> ReadAsync(SpecRun spec, IReadOnlyList<TicketRun> tickets, CancellationToken cancellationToken)
    {
        var eligible = new List<IssueRef>();
        var blocked = new List<IssueRef>();
        var runnable = new HashSet<TicketRunId>();
        var errors = new List<GitHubIssuesException>();
        string? waitReason = null;
        try
        {
            IssueSnapshot parent = await issues.GetIssueAsync(spec.ParentIssue, cancellationToken);
            IReadOnlyList<IssueSnapshot> children = await issues.GetSubIssuesAsync(spec.ParentIssue, cancellationToken);
            if (!parent.IsOpenAndOptedIn())
            {
                string stopReason = parent.State != IssueState.Open
                    ? $"Parent spec {spec.ParentIssue} is closed. Selected tickets may finish; queue again explicitly after reopening and opting in."
                    : $"Parent spec {spec.ParentIssue} opted out of WebDevLoop. Selected tickets may finish; opt in and queue again explicitly to start new work. Branches and pull requests are preserved.";
                return new CurrentWork(stopReason, null, eligible, runnable, errors);
            }

            if (!await _blockers.AreAllSatisfiedAsync(parent, cancellationToken))
            {
                waitReason = $"Parent spec {spec.ParentIssue} has open native blockers; new work and parent review are deferred.";
            }

            foreach (IssueSnapshot child in children)
            {
                TicketRun? ticket = tickets.LastOrDefault(ticket => SpecIssues.AreSame(ticket.Issue, child.Ref));
                if (ticket is { IsTerminal: true } or { IsSelectedAndOutstanding: true })
                {
                    continue;
                }

                try
                {
                    IssueSnapshot current = await issues.GetIssueAsync(child.Ref, cancellationToken);
                    if (!current.IsOpenAndOptedIn())
                    {
                        continue;
                    }

                    eligible.Add(current.Ref);
                    if (!await _blockers.AreAllSatisfiedAsync(current, cancellationToken))
                    {
                        blocked.Add(current.Ref);
                    }
                    else if (ticket is not null && waitReason is null)
                    {
                        runnable.Add(ticket.Id);
                    }
                }
                catch (GitHubIssuesException exception)
                {
                    errors.Add(new GitHubIssuesException($"Current GitHub eligibility/blocker check for ticket {child.Ref} failed; work deferred for recovery. {exception.Message}", exception));
                }
            }
        }
        catch (GitHubIssuesException exception)
        {
            errors.Add(new GitHubIssuesException($"Could not verify current parent/sub-issue eligibility for {spec.ParentIssue}; work deferred for recovery. {exception.Message}", exception));
        }

        return new CurrentWork(null, waitReason, eligible, runnable, errors) { Blocked = blocked };
    }
}
