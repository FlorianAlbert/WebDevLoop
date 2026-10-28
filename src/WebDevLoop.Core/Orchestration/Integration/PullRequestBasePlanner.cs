using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>Chooses the base branch for a ticket's PR layer so the PRs form a valid base-to-head chain.</summary>
public static class PullRequestBasePlanner
{
    /// <param name="previousLayerHead">Head branch of the layer directly below in the same spec, if any.</param>
    public static BranchName PlanBase(
        BranchName trunk,
        BranchName? previousLayerHead) => previousLayerHead ?? trunk;
}
