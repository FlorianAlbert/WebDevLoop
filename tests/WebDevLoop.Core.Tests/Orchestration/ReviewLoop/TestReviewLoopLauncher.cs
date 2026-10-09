using WebDevLoop.Core.Orchestration.ReviewLoop;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

/// <summary>Records launched review loops without running them.</summary>
internal sealed class TestReviewLoopLauncher : IReviewLoopLauncher
{
    public List<ReviewAssignment> Launched { get; } = [];

    public void Launch(ReviewAssignment assignment) => Launched.Add(assignment);
}
