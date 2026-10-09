namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>
/// Starts a spec's parent-spec review in the background so event handling never waits for reviewers or GitHub.
/// Implementations run <see cref="ParentSpecReviewRunner.RunAsync"/> in a fresh unit-of-work scope and must return
/// immediately. Launching the same assignment twice is safe: review turns and finding issuances are claimed once.
/// </summary>
public interface IParentReviewLauncher
{
    void Launch(ParentReviewAssignment assignment);
}
