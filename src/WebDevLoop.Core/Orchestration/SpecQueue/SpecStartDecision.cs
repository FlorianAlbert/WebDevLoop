using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

public enum SpecStartKind
{
    /// <summary>No unmerged blocker: branch the integration branch from current trunk.</summary>
    FromTrunk,

    /// <summary>Branch the integration branch from the single unmerged blocker's ready integration tip.</summary>
    StackOnTop,

    /// <summary>At least one blocker is not merged and the dependency mode does not allow starting yet.</summary>
    Wait,
}

/// <param name="StackBaseSha">The blocker's integration tip when <paramref name="Kind"/> is <see cref="SpecStartKind.StackOnTop"/>.</param>
public sealed record SpecStartDecision(SpecStartKind Kind, CommitSha? StackBaseSha = null)
{
    public static SpecStartDecision FromTrunk { get; } = new(SpecStartKind.FromTrunk);

    public static SpecStartDecision Wait { get; } = new(SpecStartKind.Wait);

    public static SpecStartDecision OnTopOf(CommitSha blockerTip) => new(SpecStartKind.StackOnTop, blockerTip);
}
