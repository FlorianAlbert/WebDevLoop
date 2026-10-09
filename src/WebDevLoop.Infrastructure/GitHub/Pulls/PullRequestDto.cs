using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

internal sealed record GitRefDto(string Ref, string? Sha);

internal sealed record PullRequestDto(
    int Number,
    string NodeId,
    string State,
    bool Draft,
    DateTimeOffset? MergedAt,
    string? MergeCommitSha,
    string? Body,
    GitRefDto Head,
    GitRefDto Base)
{
    public PullRequestState PullState => State switch
    {
        "open" => PullRequestState.Open,
        _ => MergedAt is null ? PullRequestState.Closed : PullRequestState.Merged,
    };

    public PullRequestSnapshot ToSnapshot() => new(
        new PullRequestNumber(Number),
        new BranchName(Head.Ref),
        new BranchName(Base.Ref),
        new CommitSha(Head.Sha ?? throw new InvalidOperationException($"Pull request #{Number} has no head SHA.")),
        Body ?? string.Empty,
        Draft,
        PullState);
}
