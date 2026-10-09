using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>The status a step's owner is in while that step's work is in progress.</summary>
internal static class WorkingStatus
{
    public static TicketRunStatus? OfTicketFor(StepKind kind) => kind switch
    {
        StepKind.Implement => TicketRunStatus.Implementing,
        StepKind.Fix => TicketRunStatus.FixingReviewFindings,
        StepKind.Review => TicketRunStatus.Reviewing,
        StepKind.ResolveConflict => TicketRunStatus.Integrating,
        _ => null,
    };

    public static SpecRunStatus? OfSpecFor(StepKind kind) => kind switch
    {
        StepKind.Explore => SpecRunStatus.Preparing,
        StepKind.ParentReview => SpecRunStatus.ParentReviewing,
        StepKind.Test => SpecRunStatus.Testing,
        _ => null,
    };
}
