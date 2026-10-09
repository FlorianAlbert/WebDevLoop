using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <param name="BlockedBy">Native GitHub "blocked by" relationships of this issue.</param>
public sealed record IssueSnapshot(
    IssueRef Ref,
    string Title,
    string Body,
    IssueState State,
    IReadOnlyList<IssueRef> BlockedBy);
