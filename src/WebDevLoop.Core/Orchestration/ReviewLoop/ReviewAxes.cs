using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>Each review axis is owned by its own reviewer role.</summary>
public static class ReviewAxes
{
    public static AgentRole ReviewerFor(FindingAxis axis) => axis switch
    {
        FindingAxis.CodingStandards => AgentRole.ReviewerCodingStandards,
        FindingAxis.Specification => AgentRole.ReviewerSpecification,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Only coding standards and specification are review axes."),
    };
}
