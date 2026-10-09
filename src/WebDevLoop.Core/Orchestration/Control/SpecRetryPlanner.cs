using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>Where a retried spec resumes, from the phase it needed attention in.</summary>
public static class SpecRetryPlanner
{
    /// <param name="hasOpenTickets">Some ticket is not done yet, e.g. finding tickets the last parent review or test cycle created.</param>
    /// <returns>
    /// The failed phase again (preparation, parent review, testing, or merge tracking via <c>ReadyForReview</c>), or <c>Running</c> when open tickets must
    /// be worked first (the next parent-review cycle then starts by itself). Null when the spec failed before it was
    /// started and has to be queued again.
    /// </returns>
    public static SpecRunStatus? Target(SpecRun spec, bool hasOpenTickets)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.NeedsAttentionFrom switch
        {
            // Recorded before the failed phase was persisted: resume from what the run already has.
            null => spec.IntegrationBaseSha is null ? SpecRunStatus.Preparing : SpecRunStatus.Running,
            SpecRunStatus.Preparing => SpecRunStatus.Preparing,
            SpecRunStatus.Running => SpecRunStatus.Running,
            SpecRunStatus.ParentReviewing or SpecRunStatus.Testing when hasOpenTickets => SpecRunStatus.Running,
            SpecRunStatus.ParentReviewing => SpecRunStatus.ParentReviewing,
            SpecRunStatus.Testing => SpecRunStatus.Testing,
            // Merge tracking restarts from ReadyForReview (idempotent completion), which resets ReadyAt and with it the trunk-containment window.
            SpecRunStatus.ReadyForReview or SpecRunStatus.AwaitingMerge => SpecRunStatus.ReadyForReview,
            _ => null,
        };
    }
}
