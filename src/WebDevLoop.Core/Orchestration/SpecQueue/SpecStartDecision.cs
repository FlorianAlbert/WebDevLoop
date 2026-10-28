namespace WebDevLoop.Core.Orchestration.SpecQueue;

public enum SpecStartKind
{
    /// <summary>Every current blocker is closed: branch from current trunk.</summary>
    FromTrunk,

    /// <summary>At least one current blocker is open.</summary>
    Wait,
}

public sealed record SpecStartDecision(SpecStartKind Kind)
{
    public static SpecStartDecision FromTrunk { get; } = new(SpecStartKind.FromTrunk);

    public static SpecStartDecision Wait { get; } = new(SpecStartKind.Wait);
}
