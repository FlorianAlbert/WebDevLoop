using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>Whether a pull request found by its run-scoped head ref really is the stack layer of a given run and ticket.</summary>
internal static class PullRequestIdentity
{
    /// <summary>
    /// Matches the identity line the app writes into every layer body, or the hidden marker the GitHub adapter appends
    /// (<c>&lt;!-- webdevloop:run=… ticket=… --&gt;</c>), which survives edits of the visible text.
    /// </summary>
    public static bool Identifies(string? body, RunId runId, TicketRunId ticketRunId) =>
        !string.IsNullOrEmpty(body)
        && (body.Contains(StackLayerPullRequest.IdentityLine(runId, ticketRunId), StringComparison.Ordinal)
            || body.Contains($"webdevloop:run={runId} ticket={ticketRunId} ", StringComparison.Ordinal));
}
