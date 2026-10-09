using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <summary>Derives a spec run's merge status from its persisted state and PR stack (merge tracking keeps both current).</summary>
public static class MergeStatusProjection
{
    public static MergeStatusView From(SpecRunView spec, IReadOnlyList<StackLayerView> stack)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(stack);
        StackLayerView[] bottomToTop = stack.OrderBy(layer => layer.Position).ToArray();
        MergeState state = StateOf(spec, bottomToTop.Length > 0);
        return new MergeStatusView(
            spec.Id,
            state,
            spec.Status,
            bottomToTop.Select(layer => layer.PullRequestNumber).ToArray(),
            bottomToTop.LastOrDefault()?.CommitSha,
            spec.ReadyAt,
            spec.CompletedAt,
            state == MergeState.Closed ? spec.FailureReason : null);
    }

    private static MergeState StateOf(SpecRunView spec, bool hasStack) => spec.Status switch
    {
        SpecRunStatus.ReadyForReview or SpecRunStatus.AwaitingMerge => MergeState.Awaiting,
        SpecRunStatus.Completed => hasStack ? MergeState.Merged : MergeState.CompletedWithoutPullRequests,
        SpecRunStatus.NeedsAttention when spec.NeedsAttentionFrom == SpecRunStatus.AwaitingMerge => MergeState.Closed,
        SpecRunStatus.Aborted => MergeState.Aborted,
        _ => MergeState.NotReady,
    };
}
