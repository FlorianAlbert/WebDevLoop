using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <param name="Body">Human-readable description. The adapter appends a machine-readable marker carrying
/// <paramref name="RunId"/> and <paramref name="TicketRunId"/>, so recovery can reconcile an existing PR by head ref and identifiers.</param>
public sealed record DraftPullRequest(BranchName Head, BranchName Base, string Title, string Body, RunId RunId, TicketRunId TicketRunId);
