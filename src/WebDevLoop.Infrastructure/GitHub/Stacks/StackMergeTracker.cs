using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Pulls;

namespace WebDevLoop.Infrastructure.GitHub.Stacks;

/// <summary>
/// Derives the merge status of a stack from its PRs. <see cref="StackMergeStatus.Merged"/> additionally requires that
/// trunk really contains the top layer (checked via its merge commit, which also covers squash merges); a merged top
/// layer that trunk does not contain yet stays <see cref="StackMergeStatus.Open"/> so the caller keeps polling.
/// </summary>
internal sealed class StackMergeTracker(PullRequestsClient pulls)
{
    public async Task<StackMergeStatus> GetStatusAsync(
        GitHubRepoRef repo,
        IReadOnlyList<PullRequestNumber> bottomToTop,
        BranchName trunk,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bottomToTop);
        if (bottomToTop.Count == 0)
        {
            throw new ArgumentException("A stack needs at least one pull request.", nameof(bottomToTop));
        }

        var layers = new List<PullRequestDto>();
        foreach (PullRequestNumber number in bottomToTop)
        {
            layers.Add(await pulls.GetAsync(repo, number, cancellationToken));
        }

        if (layers.Any(layer => layer.PullState == PullRequestState.Closed))
        {
            return StackMergeStatus.ClosedUnmerged;
        }

        if (layers.Any(layer => layer.PullState == PullRequestState.Open))
        {
            return StackMergeStatus.Open;
        }

        PullRequestDto top = layers[^1];
        string topCommit = top.MergeCommitSha ?? top.Head.Sha!;
        return await pulls.BranchContainsAsync(repo, trunk, topCommit, cancellationToken)
            ? StackMergeStatus.Merged
            : StackMergeStatus.Open;
    }
}
