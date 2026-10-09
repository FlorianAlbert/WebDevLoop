using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>An actionable review finding or tester issue. Parent-review and tester findings become finding tickets.</summary>
public abstract record Finding
{
    private protected Finding()
    {
    }

    public abstract FindingAxis Axis { get; }

    /// <summary>Short statement of the problem, used as the finding ticket title.</summary>
    public abstract string Title { get; }
}
