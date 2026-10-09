using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public sealed record PullRequestSnapshot(
    PullRequestNumber Number,
    BranchName Head,
    BranchName Base,
    CommitSha HeadSha,
    string Body,
    bool IsDraft,
    PullRequestState State);
