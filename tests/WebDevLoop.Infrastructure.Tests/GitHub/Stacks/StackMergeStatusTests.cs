using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Pulls;
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
    public async Task The_trunk_comparison_requests_contents_read_in_addition_to_pull_request_write()
    {
        PullState(11, "closed", merged: true, mergeSha: Sha('b'));
        PullState(12, "closed", merged: true, mergeSha: TopMergeSha);
        TrunkCompare(TopMergeSha, "ahead");

        await _h.Adapter.GetStackMergeStatusAsync(Repo, Layers, Trunk, CancellationToken.None);

        GitHubPermissionSet expected = GitHubPermissionSet.PullRequestsWrite.With("contents", GitHubPermissionLevel.Read);
        Assert.Equal(expected, _h.Tokens.Requests[^1].Permissions);
        Assert.Contains(_h.Tokens.Requests, request => request.Permissions.Equals(GitHubPermissionSet.PullRequestsWrite));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_failing_trunk_comparison_is_an_error_and_not_reported_as_not_yet_merged(HttpStatusCode status)
    {
        PullState(11, "closed", merged: true, mergeSha: Sha('b'));
        PullState(12, "closed", merged: true, mergeSha: TopMergeSha);
        _h.Handler.Respond(HttpMethod.Get, RepoPath($"compare/{TopMergeSha}...main"), status, new { message = "Resource not accessible by integration" });

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(
            () => _h.Adapter.GetStackMergeStatusAsync(Repo, Layers, Trunk, CancellationToken.None));

        Assert.Equal(status, error.StatusCode);
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
