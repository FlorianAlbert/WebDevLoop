using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

public enum SpecStartKind
{
    /// <summary>Every current blocker is closed: branch from current trunk.</summary>
    FromTrunk,

    /// <summary>At least one current blocker is open.</summary>
    Wait,
}

/// <summary>The blocker decision together with the parent snapshot it was evaluated from.</summary>
public sealed record SpecStartDecision(SpecStartKind Kind, IssueSnapshot Parent);
