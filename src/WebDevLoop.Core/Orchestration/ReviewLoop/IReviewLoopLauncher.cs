namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Starts a ticket's review/fix loop in the background so event handling never waits for reviewers. Implementations run
/// <see cref="TicketReviewLoop.RunAsync"/> in a fresh unit-of-work scope and must return immediately. Launching the same
/// assignment twice is safe: each review round and fix turn is claimed once.
/// </summary>
public interface IReviewLoopLauncher
{
    void Launch(ReviewAssignment assignment);
}
