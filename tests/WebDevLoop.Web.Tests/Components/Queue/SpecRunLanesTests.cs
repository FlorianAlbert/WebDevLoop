using WebDevLoop.Core.Domain;
using WebDevLoop.Web.Components.Queue;

namespace WebDevLoop.Web.Tests.Components.Queue;

public sealed class SpecRunLanesTests
{
    [Theory]
    [InlineData(SpecRunStatus.Preparing, SpecRunLane.Active)]
    [InlineData(SpecRunStatus.Running, SpecRunLane.Active)]
    [InlineData(SpecRunStatus.ParentReviewing, SpecRunLane.Active)]
    [InlineData(SpecRunStatus.Testing, SpecRunLane.Active)]
    [InlineData(SpecRunStatus.Queued, SpecRunLane.Waiting)]
    [InlineData(SpecRunStatus.WaitingForDependency, SpecRunLane.Waiting)]
    [InlineData(SpecRunStatus.ReadyForReview, SpecRunLane.AwaitingMerge)]
    [InlineData(SpecRunStatus.AwaitingMerge, SpecRunLane.AwaitingMerge)]
    [InlineData(SpecRunStatus.NeedsAttention, SpecRunLane.NeedsAttention)]
    [InlineData(SpecRunStatus.Completed, SpecRunLane.Completed)]
    [InlineData(SpecRunStatus.Aborted, SpecRunLane.Completed)]
    public void every_status_maps_to_one_lane(SpecRunStatus status, SpecRunLane expected) =>
        Assert.Equal(expected, SpecRunLanes.For(status));

    [Fact]
    public void every_status_is_classified_consistently_with_the_domain_active_slot_rule()
    {
        foreach (SpecRunStatus status in Enum.GetValues<SpecRunStatus>())
        {
            Assert.Equal(status.IsActive(), SpecRunLanes.For(status) == SpecRunLane.Active);
        }
    }
}
