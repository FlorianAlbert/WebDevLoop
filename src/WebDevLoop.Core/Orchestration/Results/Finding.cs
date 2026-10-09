using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>An actionable review finding or tester issue. Parent-review and tester findings become finding tickets.</summary>
public abstract record Finding
{
    private protected Finding(string? id, IReadOnlyList<string>? blockedBy)
    {
        Id = ReportGuard.OptionalText(id)?.Trim();
        BlockedBy = ReportGuard.RequireDependencies(Id, blockedBy);
    }

    public abstract FindingAxis Axis { get; }

    /// <summary>Short statement of the problem, used as the finding ticket title.</summary>
    public abstract string Title { get; }

    /// <summary>Reporter-assigned identifier (unique within the report) that other findings name in <see cref="BlockedBy"/>.</summary>
    public string? Id { get; }

    /// <summary>Ids of findings in the same report that must be fixed before this one; empty when it is independent.</summary>
    public IReadOnlyList<string> BlockedBy { get; }
}
