using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Tests.GitHub.Pulls;
using static WebDevLoop.Infrastructure.Tests.GitHub.Pulls.PullsHarness;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Stacks;

public sealed class StackMergeStatusTests
{
    private static readonly string TopMergeSha = Sha('c');

    private readonly PullsHarness _h = new();

    private static IReadOnlyList<PullRequestNumber> Layers => [Pr(11), Pr(12)];

    private void PullState(int number, string state, bool merged, string? mergeSha = null) =>
        _h.Handler.Respond(
            HttpMethod.Get,
            RepoPath($"pulls/{number}"),
            HttpStatusCode.OK,
            PullJson(number, $"stack/run-1/t{number}", "main", draft: false, state: state, merged: merged, mergeSha: mergeSha));

    private void TrunkCompare(string sha, string status) =>
        _h.Handler.Respond(HttpMethod.Get, RepoPath($"compare/{sha}...main"), HttpStatusCode.OK, new { status });

    [Fact]
    public async Task Open_while_any_layer_is_still_open()
    {
        PullState(11, "closed", merged: true, mergeSha: Sha('b'));
        PullState(12, "open", merged: false);

        Assert.Equal(StackMergeStatus.Open, await _h.Adapter.GetStackMergeStatusAsync(Repo, Layers, Trunk, CancellationToken.None));
    }

    [Theory]
    [InlineData("ahead")]
    [InlineData("identical")]
    public async Task Merged_when_all_layers_are_merged_and_trunk_contains_the_top_layer(string compareStatus)
    {
        PullState(11, "closed", merged: true, mergeSha: Sha('b'));
        PullState(12, "closed", merged: true, mergeSha: TopMergeSha);
        TrunkCompare(TopMergeSha, compareStatus);

        Assert.Equal(StackMergeStatus.Merged, await _h.Adapter.GetStackMergeStatusAsync(Repo, Layers, Trunk, CancellationToken.None));
    }

    [Theory]
    [InlineData("behind")]
    [InlineData("diverged")]
    public async Task Stays_open_when_all_layers_are_merged_but_trunk_does_not_contain_the_top_layer_yet(string compareStatus)
    {
        PullState(11, "closed", merged: true, mergeSha: Sha('b'));
        PullState(12, "closed", merged: true, mergeSha: TopMergeSha);
        TrunkCompare(TopMergeSha, compareStatus);

        Assert.Equal(StackMergeStatus.Open, await _h.Adapter.GetStackMergeStatusAsync(Repo, Layers, Trunk, CancellationToken.None));
    }

    [Fact]
    public async Task Closed_unmerged_layer_maps_to_closed_unmerged_even_when_other_layers_merged()
    {
        PullState(11, "closed", merged: true, mergeSha: Sha('b'));
        PullState(12, "closed", merged: false);

        Assert.Equal(StackMergeStatus.ClosedUnmerged, await _h.Adapter.GetStackMergeStatusAsync(Repo, Layers, Trunk, CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_layer_list_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _h.Adapter.GetStackMergeStatusAsync(Repo, [], Trunk, CancellationToken.None));
    }
}
