using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public sealed record WorktreeInspection(WorktreeStatus Status, BranchName? Branch, CommitSha? Head);
