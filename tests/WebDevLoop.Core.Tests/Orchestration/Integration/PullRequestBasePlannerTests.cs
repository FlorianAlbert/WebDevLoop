using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class PullRequestBasePlannerTests
{
    private static readonly BranchName Trunk = new("main");
    private static readonly BranchName PreviousLayer = new("stack/run-2/t1");

    [Fact]
    public void First_layer_targets_trunk() =>
        Assert.Equal(Trunk, PullRequestBasePlanner.PlanBase(Trunk, null));

    [Fact]
    public void Upper_layers_target_the_previous_layer_of_the_same_spec() =>
        Assert.Equal(PreviousLayer, PullRequestBasePlanner.PlanBase(Trunk, PreviousLayer));
}
