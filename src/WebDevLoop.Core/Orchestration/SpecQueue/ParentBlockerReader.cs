using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Reads and evaluates native blockers for work decisions and display, using direct or request-cached issue reads.</summary>
public sealed class ParentBlockerReader(Func<IssueRef, CancellationToken, Task<IssueSnapshot>> readIssue)
{
    public async Task<IReadOnlyList<ParentBlocker>> ReadAsync(IssueRef parent, CancellationToken cancellationToken)
    {
        IssueSnapshot snapshot = await ReadIssueAsync(parent, parent, cancellationToken);
        var blockers = new List<ParentBlocker>();
        foreach (IssueRef issue in snapshot.BlockedBy)
        {
            blockers.Add(await ReadBlockerAsync(issue, parent, cancellationToken));
        }

        return blockers;
    }

    public async Task<bool> AreAllSatisfiedAsync(IssueRef issue, CancellationToken cancellationToken) =>
        await AreAllSatisfiedAsync(await ReadIssueAsync(issue, issue, cancellationToken), cancellationToken);

    /// <summary>Evaluates the decision's current relationships without re-reading the issue itself.</summary>
    public async Task<bool> AreAllSatisfiedAsync(IssueSnapshot current, CancellationToken cancellationToken)
    {
        foreach (IssueRef issue in current.BlockedBy)
        {
            if (!(await ReadBlockerAsync(issue, current.Ref, cancellationToken)).IsSatisfied)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<ParentBlocker> ReadBlockerAsync(IssueRef issue, IssueRef blockedIssue, CancellationToken cancellationToken)
    {
        IssueSnapshot blocker = await ReadIssueAsync(issue, blockedIssue, cancellationToken);
        return new ParentBlocker(issue, blocker.Title, blocker.State);
    }

    private async Task<IssueSnapshot> ReadIssueAsync(IssueRef issue, IssueRef blockedIssue, CancellationToken cancellationToken)
    {
        try
        {
            return await readIssue(issue, cancellationToken);
        }
        catch (GitHubIssuesException exception)
        {
            throw new GitHubIssuesException(
                $"Cannot verify current GitHub blockers for {blockedIssue}: could not read {issue}. "
                + $"Check issue access and GitHub availability, then refresh or retry queue recovery. "
                + $"Unknown blocker state never permits work. {exception.Message}",
                exception);
        }
    }
}

/// <summary>A current native blocker read from GitHub, without local tracked-run presentation data.</summary>
public sealed record ParentBlocker(IssueRef Issue, string Title, IssueState State)
{
    public bool IsSatisfied => State == IssueState.Closed;
}
