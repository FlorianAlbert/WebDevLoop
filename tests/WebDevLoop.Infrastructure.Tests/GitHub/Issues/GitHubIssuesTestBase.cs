using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Issues;

public abstract class GitHubIssuesTestBase
{
    internal const string TokenValue = "ghs_issues";

    private protected FakeIssuesApiHandler Api { get; } = new();

    private protected RecordingTokenProvider Tokens { get; } = new(
        GitHubTokenResult.Available(new GitHubAccessToken(TokenValue, GitHubTokenKind.AppInstallation, "42", 1, null)));

    private protected GitHubIssues CreateSut(ITokenProvider? tokens = null) => new(new HttpClient(Api), tokens ?? Tokens);

    internal static IssueRef Ref(int number, long? databaseId = null) => new("acme", "widgets", number, databaseId: databaseId);

    internal static string IssuePath(int number) => $"/repos/acme/widgets/issues/{number}";

    internal static string SubIssuesPath(int number) => $"{IssuePath(number)}/sub_issues";

    internal static string BlockedByPath(int number) => $"{IssuePath(number)}/dependencies/blocked_by";
}
