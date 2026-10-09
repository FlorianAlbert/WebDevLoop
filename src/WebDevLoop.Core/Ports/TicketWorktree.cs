using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public sealed record TicketWorktree(string Path, BranchName Branch, CommitSha Head);
