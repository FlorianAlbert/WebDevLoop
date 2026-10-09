using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <param name="Body">Must carry the run and ticket identifiers so recovery can reconcile an existing PR.</param>
public sealed record DraftPullRequest(BranchName Head, BranchName Base, string Title, string Body);
