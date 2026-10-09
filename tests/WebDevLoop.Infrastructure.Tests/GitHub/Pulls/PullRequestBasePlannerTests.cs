using WebDevLoop.Core.Domain;
using WebDevLoop.Infrastructure.GitHub.Pulls;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Pulls;

public sealed class PullRequestBasePlannerTests
{
    private static readonly BranchName Trunk = new("main");
    private static readonly BranchName BlockingTop = new("stack/run-1/t9");
    private static readonly BranchName PreviousLayer = new("stack/run-2/t1");

    [Fact]
    public void First_layer_of_a_wait_for_merge_spec_targets_trunk()
    {
        Assert.Equal(Trunk, PullRequestBasePlanner.PlanBase(SpecDependencyMode.WaitForMerge, Trunk, BlockingTop, null));
    }

    [Fact]
    public void First_layer_of_a_stack_on_top_spec_targets_the_blocking_stack_branch()
    {
        Assert.Equal(BlockingTop, PullRequestBasePlanner.PlanBase(SpecDependencyMode.StackOnTop, Trunk, BlockingTop, null));
    }

    [Fact]
    public void First_layer_of_a_stack_on_top_spec_without_a_blocking_stack_targets_trunk()
    {
        Assert.Equal(Trunk, PullRequestBasePlanner.PlanBase(SpecDependencyMode.StackOnTop, Trunk, null, null));
    }

    [Theory]
    [InlineData(SpecDependencyMode.WaitForMerge)]
    [InlineData(SpecDependencyMode.StackOnTop)]
    public void Upper_layers_target_the_previous_layer_of_the_same_spec(SpecDependencyMode mode)
    {
        Assert.Equal(PreviousLayer, PullRequestBasePlanner.PlanBase(mode, Trunk, BlockingTop, PreviousLayer));
    }
}
