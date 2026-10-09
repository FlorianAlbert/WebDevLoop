namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

public sealed record ReadyAndMergeOptions
{
    /// <summary>
    /// How long trunk may lag behind a stack whose PRs are all merged before the spec needs attention (e.g. the top layer
    /// was merged into an intermediate branch that never reached trunk).
    /// </summary>
    public TimeSpan TrunkContainmentTimeout { get; init; } = TimeSpan.FromHours(1);
}
