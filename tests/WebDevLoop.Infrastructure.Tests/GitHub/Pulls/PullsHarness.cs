using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Pulls;
using WebDevLoop.Infrastructure.GitHub.Stacks;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Pulls;

internal sealed class PullsHarness
{
    public const string Token = "ghs_pulls";
    public const string GraphQlPath = "/graphql";

    public static readonly GitHubRepoRef Repo = new("acme", "widgets");
    public static readonly RunId Run = new("run-1");
    public static readonly TicketRunId Ticket = new("t1");
    public static readonly BranchName Trunk = new("main");

    public PullsHarness(GitHubTokenResult? tokenResult = null, IGhCommandRunner? gh = null)
    {
        Tokens = new RecordingTokenProvider(tokenResult ?? GitHubTokenResult.Available(
            new GitHubAccessToken(Token, "octocat", 1, null)));
        Adapter = new GitHubPullsAndStacks(new HttpClient(Handler), Tokens, new GitHubPullsOptions(), gh);
    }

    public StubGitHubHandler Handler { get; } = new();

    public RecordingTokenProvider Tokens { get; }

    public GitHubPullsAndStacks Adapter { get; }

    public static string RepoPath(string suffix) => $"/repos/acme/widgets/{suffix}";

    public static string Sha(char c) => new(c, 40);

    public static object PullJson(
        int number,
        string head,
        string baseRef,
        string? body = null,
        bool draft = true,
        string state = "open",
        bool merged = false,
        char sha = 'a',
        string? mergeSha = null) => new
        {
            number,
            node_id = $"PR_node_{number}",
            state,
            draft,
            merged_at = merged ? "2026-01-01T00:00:00Z" : null,
            merge_commit_sha = mergeSha,
            body,
            head = new { @ref = head, sha = Sha(sha) },
            @base = new { @ref = baseRef },
        };

    public static string OwnBody(string text = "Implements ticket") => $"{text}\n\n{PullRequestMarker.Format(Run, Ticket)}";

    public static PullRequestNumber Pr(int number) => new(number);

    public static object StackJson(int number, params int[] bottomToTop) => new
    {
        id = number + 1000,
        number,
        node_id = $"S_{number}",
        open = true,
        pull_requests = bottomToTop.Select(n => new { number = n, state = "open", draft = true }).ToArray(),
    };
}
