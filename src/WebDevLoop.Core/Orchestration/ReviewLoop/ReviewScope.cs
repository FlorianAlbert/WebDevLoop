namespace WebDevLoop.Core.Orchestration.ReviewLoop;

public enum ReviewScope
{
    /// <summary>Workflow step 5: one ticket branch before it is squash-merged onto the integration branch.</summary>
    Ticket,

    /// <summary>Workflow step 9: the final parent-spec review of the whole integration branch.</summary>
    ParentSpec,
}
