using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Verifies a spec's PR stack before it is marked ready (workflow step 8): layer order, trunk, linear ancestry from the
/// integration base through every layer to the integration tip, the GitHub stack order, and that every PR still shows exactly
/// its ticket's squash commit (open, unchanged head, base containing the layer below but not the layer itself, diff verified
/// by the integration saga).
/// </summary>
internal sealed class StackVerifier(IGitWorkspace git, IGitHubPullsAndStacks pulls)
{
    /// <param name="layers">The spec's layers bottom to top; at least one.</param>
    /// <returns>Null when the stack may be marked ready; otherwise what is wrong.</returns>
    public async Task<string?> FindProblemAsync(
        SpecRun spec,
        GitRepositoryLocation location,
        IReadOnlyList<PullStackLayer> layers,
        CommitSha integrationTip,
        CancellationToken cancellationToken)
    {
        if (spec.BaseBranch is not { } trunk)
        {
            return $"spec run '{spec.Id}' has no trunk branch.";
        }

        PullStackLayer top = layers[^1];
        if (top.CommitSha != integrationTip)
        {
            return $"the top layer #{top.PullRequestNumber} at {top.CommitSha} is not the integration tip {integrationTip}; an integrated commit has no PR layer.";
        }

        await git.FetchAsync(location, cancellationToken);
        return FindOrderProblem(spec, layers, trunk)
            ?? await FindAncestryProblemAsync(spec, location, layers, cancellationToken)
            ?? await FindGitHubStackProblemAsync(spec, location, layers, cancellationToken)
            ?? await FindPullRequestProblemsAsync(spec, location, layers, trunk, cancellationToken);
    }

    private static string? FindOrderProblem(SpecRun spec, IReadOnlyList<PullStackLayer> layers, BranchName trunk)
    {
        for (int index = 0; index < layers.Count; index++)
        {
            PullStackLayer layer = layers[index];
            if (layer.Position != index + 1)
            {
                return $"layer #{layer.PullRequestNumber} is at position {layer.Position} instead of {index + 1}.";
            }

            BranchName? expectedBase = index > 0 ? layers[index - 1].BranchName
                : spec.DependencyModeUsed == SpecDependencyMode.StackOnTop ? null
                : trunk;
            if (expectedBase is { } required && layer.BaseBranch != required)
            {
                return $"layer #{layer.PullRequestNumber} is based on '{layer.BaseBranch}' instead of '{required}'.";
            }
        }

        return null;
    }

    private async Task<string?> FindAncestryProblemAsync(
        SpecRun spec,
        GitRepositoryLocation location,
        IReadOnlyList<PullStackLayer> layers,
        CancellationToken cancellationToken)
    {
        CommitSha? below = spec.IntegrationBaseSha;
        foreach (PullStackLayer layer in layers)
        {
            if (below is { } parent && (parent == layer.CommitSha || !await git.IsAncestorAsync(location, parent, layer.CommitSha, cancellationToken)))
            {
                return $"layer commit {layer.CommitSha} of #{layer.PullRequestNumber} does not build linearly on {parent}.";
            }

            below = layer.CommitSha;
        }

        return null;
    }

    /// <summary>A single PR on trunk is an ordinary PR; otherwise the spec's PRs must be linked into one stack in layer order.</summary>
    private async Task<string?> FindGitHubStackProblemAsync(
        SpecRun spec,
        GitRepositoryLocation location,
        IReadOnlyList<PullStackLayer> layers,
        CancellationToken cancellationToken)
    {
        if (layers.Count == 1 && spec.DependencyModeUsed != SpecDependencyMode.StackOnTop)
        {
            return null;
        }

        PullRequestNumber[] expected = layers.Select(layer => layer.PullRequestNumber).ToArray();
        PullStackSnapshot? stack = await pulls.FindStackAsync(location.Repo, expected[0], cancellationToken);
        if (stack is null)
        {
            return $"pull request #{expected[0]} is not linked into a GitHub stack.";
        }

        int bottom = stack.BottomToTop.ToList().IndexOf(expected[0]);
        return stack.BottomToTop.Skip(bottom).Take(expected.Length).SequenceEqual(expected)
            ? null
            : $"the GitHub stack {stack.StackNumber} lists {PullRequestNumbers.Describe(stack.BottomToTop)} instead of {PullRequestNumbers.Describe(expected)} in this order.";
    }

    private async Task<string?> FindPullRequestProblemsAsync(
        SpecRun spec,
        GitRepositoryLocation location,
        IReadOnlyList<PullStackLayer> layers,
        BranchName trunk,
        CancellationToken cancellationToken)
    {
        CommitSha? below = spec.IntegrationBaseSha;
        foreach (PullStackLayer layer in layers)
        {
            bool isBottom = layer.Position == 1;
            if (await FindPullRequestProblemAsync(location, layer, below, isBottom ? trunk : null, cancellationToken) is { } problem)
            {
                return problem;
            }

            below = layer.CommitSha;
        }

        return null;
    }

    /// <param name="alternativeBase">For the bottom layer: trunk, which GitHub retargets it to once the stack below it merged.</param>
    private async Task<string?> FindPullRequestProblemAsync(
        GitRepositoryLocation location,
        PullStackLayer layer,
        CommitSha? below,
        BranchName? alternativeBase,
        CancellationToken cancellationToken)
    {
        PullRequestNumber number = layer.PullRequestNumber;
        PullRequestSnapshot pull = await pulls.GetPullRequestAsync(location.Repo, number, cancellationToken);
        if (pull.State != PullRequestState.Open)
        {
            return $"pull request #{number} is {pull.State}.";
        }

        if (pull.Head != layer.BranchName || pull.HeadSha != layer.CommitSha)
        {
            return $"pull request #{number} has head commit {pull.HeadSha} on '{pull.Head}' instead of {layer.CommitSha} on '{layer.BranchName}'.";
        }

        if (pull.Base != layer.BaseBranch && pull.Base != alternativeBase)
        {
            return $"pull request #{number} targets '{pull.Base}' instead of '{layer.BaseBranch}'.";
        }

        CommitSha? stackBranchTip = await git.GetBranchTipAsync(location, layer.BranchName, GitRefScope.Remote, cancellationToken);
        if (stackBranchTip != layer.CommitSha)
        {
            return $"stack branch '{layer.BranchName}' is at {stackBranchTip?.Value ?? "(missing)"} instead of {layer.CommitSha}.";
        }

        CommitSha? baseTip = await git.GetBranchTipAsync(location, pull.Base, GitRefScope.Remote, cancellationToken);
        if (baseTip is not { } baseHead
            || (below is { } parent && !await git.IsAncestorAsync(location, parent, baseHead, cancellationToken))
            || await git.IsAncestorAsync(location, layer.CommitSha, baseHead, cancellationToken))
        {
            return $"base '{pull.Base}' of pull request #{number} at {baseTip?.Value ?? "(missing)"} must contain {below?.Value ?? "the integration base"} "
                + $"but not the layer commit {layer.CommitSha}, so its diff would not be exactly the ticket's changes.";
        }

        if (layer.VerifiedDiffSha != layer.CommitSha)
        {
            return $"the diff of pull request #{number} was never verified for {layer.CommitSha}.";
        }

        return below is { } from && (await git.GetChangedFilesAsync(location, from, layer.CommitSha, cancellationToken)).Count == 0
            ? $"layer commit {layer.CommitSha} of pull request #{number} changes no files."
            : null;
    }
}
