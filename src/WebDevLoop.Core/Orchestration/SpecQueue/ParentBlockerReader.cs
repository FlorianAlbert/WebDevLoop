using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Reads current native parent blockers for scheduling and display, using direct or request-cached issue reads.</summary>
public sealed class ParentBlockerReader(Func<IssueRef, CancellationToken, Task<IssueSnapshot>> readIssue)
{
    public async Task<IReadOnlyList<ParentBlocker>> ReadAsync(IssueRef parent, CancellationToken cancellationToken)
    {
        IssueSnapshot snapshot = await ReadIssueAsync(parent, parent, cancellationToken);
        var blockers = new List<ParentBlocker>();
        foreach (IssueRef issue in snapshot.BlockedBy)
        {
            IssueSnapshot blocker = await ReadIssueAsync(issue, parent, cancellationToken);
            blockers.Add(new ParentBlocker(issue, blocker.Title, blocker.State));
        }

        return blockers;
    }

    private async Task<IssueSnapshot> ReadIssueAsync(IssueRef issue, IssueRef parent, CancellationToken cancellationToken)
    {
        try
        {
            return await readIssue(issue, cancellationToken);
        }
        catch (GitHubIssuesException exception)
        {
            throw new GitHubIssuesException(
                $"Cannot verify current GitHub blockers for {parent}: could not read {issue}. "
                + $"Check issue access and GitHub availability, then refresh or retry queue recovery. "
                + $"Unknown blocker state never permits a start. {exception.Message}",
                exception);
        }
    }
}

/// <summary>A current native blocker read from GitHub, without local tracked-run presentation data.</summary>
public sealed record ParentBlocker(IssueRef Issue, string Title, IssueState State)
{
    public bool IsSatisfied => State == IssueState.Closed;
}
