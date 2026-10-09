using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Pulls;
using WebDevLoop.Infrastructure.GitHub.Stacks;
using WebDevLoop.Infrastructure.Tests.GitHub.Pulls;
using static WebDevLoop.Infrastructure.Tests.GitHub.Pulls.PullsHarness;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Stacks;

public sealed class GitHubStackTests
{
    private static readonly string StacksPath = RepoPath("stacks");
    private static readonly string FindFor11 = RepoPath("stacks?pull_request=11");

    private readonly PullsHarness _h = new();

    private static IReadOnlyList<PullRequestNumber> Numbers(params int[] values) => values.Select(Pr).ToArray();

    [Fact]
    public async Task Find_stack_returns_the_stack_containing_the_pull_request_in_bottom_to_top_order()
    {
        _h.Handler.Respond(HttpMethod.Get, FindFor11, HttpStatusCode.OK, new[] { StackJson(5, 11, 12, 13) });

        PullStackSnapshot? stack = await _h.Adapter.FindStackAsync(Repo, Pr(11), CancellationToken.None);

        Assert.Equal(5, stack!.StackNumber);
        Assert.Equal(Numbers(11, 12, 13), stack.BottomToTop);
    }

    [Fact]
    public async Task Find_stack_returns_null_when_the_pull_request_is_not_in_a_stack()
    {
        _h.Handler.Respond(HttpMethod.Get, FindFor11, HttpStatusCode.OK, Array.Empty<object>());

        Assert.Null(await _h.Adapter.FindStackAsync(Repo, Pr(11), CancellationToken.None));
    }

    [Fact]
    public async Task Create_stack_posts_the_ordered_pull_request_numbers_bottom_to_top()
    {
        _h.Handler.Respond(HttpMethod.Post, StacksPath, HttpStatusCode.Created, StackJson(5, 11, 12, 13));

        PullStackSnapshot stack = await _h.Adapter.CreateStackAsync(Repo, Numbers(11, 12, 13), CancellationToken.None);

        RecordedCall post = Assert.Single(_h.Handler.CallsTo(HttpMethod.Post, StacksPath));
        Assert.Equal(new[] { 11, 12, 13 }, post.Json["pull_requests"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal($"Bearer {Token}", post.Authorization);
        Assert.Equal(5, stack.StackNumber);
        Assert.Equal(Numbers(11, 12, 13), stack.BottomToTop);
    }

    [Fact]
    public async Task Create_stack_requires_at_least_two_pull_requests()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _h.Adapter.CreateStackAsync(Repo, Numbers(11), CancellationToken.None));

        Assert.Empty(_h.Handler.Calls);
    }

    [Fact]
    public async Task Create_stack_returns_the_existing_stack_when_the_same_pull_requests_are_already_stacked()
    {
        _h.Handler
            .Respond(HttpMethod.Post, StacksPath, HttpStatusCode.UnprocessableEntity, new { message = "already in a stack" })
            .Respond(HttpMethod.Get, FindFor11, HttpStatusCode.OK, new[] { StackJson(5, 11, 12) });

        PullStackSnapshot stack = await _h.Adapter.CreateStackAsync(Repo, Numbers(11, 12), CancellationToken.None);

        Assert.Equal(5, stack.StackNumber);
    }

    [Fact]
    public async Task Create_stack_fails_when_the_existing_stack_has_different_members()
    {
        _h.Handler
            .Respond(HttpMethod.Post, StacksPath, HttpStatusCode.UnprocessableEntity, new { message = "already in a stack" })
            .Respond(HttpMethod.Get, FindFor11, HttpStatusCode.OK, new[] { StackJson(5, 11, 99) });

        var exception = await Assert.ThrowsAsync<GitHubApiException>(
            () => _h.Adapter.CreateStackAsync(Repo, Numbers(11, 12), CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    [Fact]
    public async Task Add_to_stack_posts_the_new_top_pull_request()
    {
        _h.Handler.Respond(HttpMethod.Post, RepoPath("stacks/5/add"), HttpStatusCode.OK, StackJson(5, 11, 12, 13));

        PullStackSnapshot stack = await _h.Adapter.AddToStackAsync(Repo, 5, Pr(13), CancellationToken.None);

        RecordedCall post = Assert.Single(_h.Handler.CallsTo(HttpMethod.Post, RepoPath("stacks/5/add")));
        Assert.Equal(new[] { 13 }, post.Json["pull_requests"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(Numbers(11, 12, 13), stack.BottomToTop);
    }

    [Fact]
    public async Task Add_to_stack_is_idempotent_when_the_pull_request_is_already_the_top_layer()
    {
        _h.Handler
            .Respond(HttpMethod.Post, RepoPath("stacks/5/add"), HttpStatusCode.UnprocessableEntity, new { message = "already in stack" })
            .Respond(HttpMethod.Get, RepoPath("stacks/5"), HttpStatusCode.OK, StackJson(5, 11, 12, 13));

        PullStackSnapshot stack = await _h.Adapter.AddToStackAsync(Repo, 5, Pr(13), CancellationToken.None);

        Assert.Equal(Numbers(11, 12, 13), stack.BottomToTop);
    }

    [Fact]
    public async Task Add_to_stack_fails_when_the_pull_request_is_not_the_top_layer_after_a_rejection()
    {
        _h.Handler
            .Respond(HttpMethod.Post, RepoPath("stacks/5/add"), HttpStatusCode.Conflict, new { message = "conflict" })
            .Respond(HttpMethod.Get, RepoPath("stacks/5"), HttpStatusCode.OK, StackJson(5, 11, 12));

        var exception = await Assert.ThrowsAsync<GitHubApiException>(
            () => _h.Adapter.AddToStackAsync(Repo, 5, Pr(13), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task Create_stack_falls_back_to_gh_stack_link_when_the_rest_endpoint_is_unavailable()
    {
        var gh = new FakeGhRunner();
        var h = new PullsHarness(gh: gh);
        h.Handler
            .Respond(HttpMethod.Post, StacksPath, HttpStatusCode.NotFound, new { message = "Not Found" })
            .Respond(HttpMethod.Get, FindFor11, HttpStatusCode.OK, new[] { StackJson(5, 11, 12) });

        PullStackSnapshot stack = await h.Adapter.CreateStackAsync(Repo, Numbers(11, 12), CancellationToken.None);

        GhInvocation call = Assert.Single(gh.Invocations);
        Assert.Equal(new[] { "stack", "link", "11", "12" }, call.Arguments);
        Assert.Equal(Token, call.Environment["GH_TOKEN"]);
        Assert.Equal("acme/widgets", call.Environment["GH_REPO"]);
        Assert.DoesNotContain(Token, string.Join(' ', call.Arguments));
        Assert.Equal(5, stack.StackNumber);
    }

    [Fact]
    public async Task Add_to_stack_falls_back_to_gh_stack_link_with_the_stack_number_first()
    {
        var gh = new FakeGhRunner();
        var h = new PullsHarness(gh: gh);
        h.Handler
            .Respond(HttpMethod.Post, RepoPath("stacks/5/add"), HttpStatusCode.NotFound, new { message = "Not Found" })
            .Respond(HttpMethod.Get, RepoPath("stacks/5"), HttpStatusCode.OK, StackJson(5, 11, 12, 13));

        PullStackSnapshot stack = await h.Adapter.AddToStackAsync(Repo, 5, Pr(13), CancellationToken.None);

        Assert.Equal(new[] { "stack", "link", "5", "13" }, Assert.Single(gh.Invocations).Arguments);
        Assert.Equal(Numbers(11, 12, 13), stack.BottomToTop);
    }

    [Fact]
    public async Task Fallback_failure_surfaces_the_gh_error()
    {
        var gh = new FakeGhRunner { Result = new(1, string.Empty, "gh: not logged in") };
        var h = new PullsHarness(gh: gh);
        h.Handler.Respond(HttpMethod.Post, StacksPath, HttpStatusCode.NotFound, new { message = "Not Found" });

        var exception = await Assert.ThrowsAsync<GhStackFallbackException>(
            () => h.Adapter.CreateStackAsync(Repo, Numbers(11, 12), CancellationToken.None));

        Assert.Contains("gh: not logged in", exception.Message);
    }

    [Fact]
    public async Task Create_stack_reports_the_original_error_when_the_endpoint_is_unavailable_and_no_fallback_is_configured()
    {
        _h.Handler.Respond(HttpMethod.Post, StacksPath, HttpStatusCode.NotFound, new { message = "Not Found" });

        var exception = await Assert.ThrowsAsync<GitHubApiException>(
            () => _h.Adapter.CreateStackAsync(Repo, Numbers(11, 12), CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    private sealed record GhInvocation(IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);

    private sealed class FakeGhRunner : IGhCommandRunner
    {
        public List<GhInvocation> Invocations { get; } = [];

        public GhCommandResult Result { get; init; } = new(0, "ok", string.Empty);

        public Task<GhCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment,
            CancellationToken cancellationToken)
        {
            Invocations.Add(new GhInvocation(arguments, environment));
            return Task.FromResult(Result);
        }
    }
}
