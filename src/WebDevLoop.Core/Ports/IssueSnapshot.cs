using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <param name="BlockedBy">Native GitHub "blocked by" relationships of this issue.</param>
public sealed record IssueSnapshot(
    IssueRef Ref,
    string Title,
    string Body,
    IssueState State,
    IReadOnlyList<IssueRef> BlockedBy)
{
    /// <summary>Labels read from GitHub for this issue only; no labels means no WebDevLoop Opt-in.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}
