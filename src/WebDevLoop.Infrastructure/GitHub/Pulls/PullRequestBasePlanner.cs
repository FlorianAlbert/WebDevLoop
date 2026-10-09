using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

/// <summary>Chooses the base branch for a ticket's PR layer so the PRs form a valid base-to-head chain.</summary>
public static class PullRequestBasePlanner
{
    /// <param name="previousLayerHead">Head branch of the layer directly below in the same spec, if any.</param>
    /// <param name="blockingStackTop">Top stack branch of the blocking spec; only used for the first layer in <see cref="SpecDependencyMode.StackOnTop"/>.</param>
    public static BranchName PlanBase(
        SpecDependencyMode mode,
        BranchName trunk,
        BranchName? blockingStackTop,
        BranchName? previousLayerHead)
    {
        if (previousLayerHead is { } previous)
        {
            return previous;
        }

        return mode == SpecDependencyMode.StackOnTop && blockingStackTop is { } blocking ? blocking : trunk;
    }
}
