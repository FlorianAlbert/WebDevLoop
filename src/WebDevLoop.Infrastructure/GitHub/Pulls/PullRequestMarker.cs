using System.Text.RegularExpressions;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

/// <summary>
/// Machine-readable PR-body marker that ties a pull request to a spec run and ticket run, e.g.
/// <c>&lt;!-- webdevloop:run=run-7 ticket=t3 --&gt;</c>. It is invisible in the rendered PR, survives edits of the
/// surrounding text, and is what recovery (WP-16/WP-18) matches on together with the exact head ref.
/// Callers must append <see cref="Format"/> to every <c>DraftPullRequest.Body</c>; the adapter rejects bodies without it.
/// </summary>
public static partial class PullRequestMarker
{
    public static string Format(RunId runId, TicketRunId ticketRunId) =>
        $"<!-- webdevloop:run={runId} ticket={ticketRunId} -->";

    /// <summary>The first well-formed marker in <paramref name="body"/>, or null when none is present.</summary>
    public static (RunId RunId, TicketRunId TicketRunId)? TryParse(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        foreach (Match match in MarkerPattern().Matches(body))
        {
            try
            {
                return (new RunId(match.Groups["run"].Value), new TicketRunId(match.Groups["ticket"].Value));
            }
            catch (ArgumentException)
            {
                // Not a valid id (for example "a..b"); keep looking for another marker.
            }
        }

        return null;
    }

    [GeneratedRegex(@"<!--\s*webdevloop:run=(?<run>[A-Za-z0-9._-]+)\s+ticket=(?<ticket>[A-Za-z0-9._-]+)\s*-->")]
    private static partial Regex MarkerPattern();
}
