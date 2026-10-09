using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>
/// Decides whether a queued spec may start, given its native GitHub spec dependencies and the effective
/// <see cref="SpecDependencyMode"/>. A blocker is merged when its run is <see cref="SpecRunStatus.Completed"/>; a blocker
/// without a usable run (never queued here, or aborted) is merged when its GitHub issue is closed.
/// </summary>
public sealed class SpecDependencyGate(ISpecRunRepository specRuns, IGitHubIssues issues)
{
    public async Task<SpecStartDecision> EvaluateAsync(
        SpecRun run,
        IReadOnlyList<SpecRun> queue,
        SpecDependencyMode mode,
        CancellationToken cancellationToken)
    {
        var unmergedRuns = new List<SpecRun>();
        bool hasOpenIssueWithoutRun = false;
        foreach (SpecDependency dependency in await specRuns.ListDependenciesAsync(run.Id, cancellationToken))
        {
            SpecRun? blocker = await FindBlockingRunAsync(dependency, queue, cancellationToken);
            if (blocker is { Status: SpecRunStatus.Completed })
            {
                continue;
            }

            if (blocker is { Status: not SpecRunStatus.Aborted })
            {
                unmergedRuns.Add(blocker);
                continue;
            }

            IssueRef blockingIssue = dependency.ExternalBlockingIssue ?? blocker!.ParentIssue;
            IssueSnapshot snapshot = await issues.GetIssueAsync(blockingIssue, cancellationToken);
            hasOpenIssueWithoutRun |= snapshot.State == IssueState.Open;
        }

        if (unmergedRuns.Count == 0 && !hasOpenIssueWithoutRun)
        {
            return SpecStartDecision.FromTrunk;
        }

        return mode == SpecDependencyMode.StackOnTop
            && !hasOpenIssueWithoutRun
            && unmergedRuns is [{ } single]
            && HasReadyStack(single)
                ? SpecStartDecision.OnTopOf(single.IntegrationTipSha!.Value)
                : SpecStartDecision.Wait;
    }

    /// <summary>Only a finished stack has a stable tip to build on; a running blocker would keep moving underneath.</summary>
    private static bool HasReadyStack(SpecRun blocker) =>
        blocker.Status is SpecRunStatus.ReadyForReview or SpecRunStatus.AwaitingMerge && blocker.IntegrationTipSha is not null;

    private async Task<SpecRun?> FindBlockingRunAsync(SpecDependency dependency, IReadOnlyList<SpecRun> queue, CancellationToken cancellationToken)
    {
        if (dependency.BlockingSpecRunId is { } blockingId)
        {
            return queue.FirstOrDefault(candidate => candidate.Id == blockingId)
                ?? await specRuns.GetAsync(blockingId, cancellationToken);
        }

        // The blocking spec may have been queued after this one; the latest non-aborted run of that issue counts.
        IssueRef external = dependency.ExternalBlockingIssue!.Value;
        return queue
            .Where(candidate => SpecIssues.AreSame(candidate.ParentIssue, external))
            .OrderByDescending(candidate => candidate.Status != SpecRunStatus.Aborted)
            .ThenByDescending(candidate => candidate.QueuePosition)
            .FirstOrDefault();
    }
}
