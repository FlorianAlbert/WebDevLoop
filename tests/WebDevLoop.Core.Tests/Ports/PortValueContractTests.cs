using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class PortValueContractTests
{
    private const string Secret = "ghs_supersecret";
    private static readonly CommitSha Sha = new(new string('a', 40));
    private static readonly GitHubRepoRef Repo = new("octo", "app");

    [Fact]
    public void conflicted_merge_requires_conflicted_paths()
    {
        Assert.Throws<ArgumentException>(() => GitMergeResult.Conflicted([]));

        GitMergeResult conflicted = GitMergeResult.Conflicted(["src/a.cs"]);
        Assert.Null(conflicted.Commit);
        Assert.Equal(["src/a.cs"], conflicted.ConflictedPaths);
    }

    [Fact]
    public void merged_result_requires_a_commit()
    {
        Assert.Throws<ArgumentException>(() => GitMergeResult.Merged(default));
        Assert.Equal(Sha, GitMergeResult.Merged(Sha).Commit);
    }

    [Fact]
    public void reported_agent_result_requires_a_report()
    {
        Assert.Throws<ArgumentNullException>(() => AgentRunResult.Reported(null!));
        Assert.IsType<ReviewReport>(AgentRunResult.Reported(ReviewReport.Clean(FindingAxis.Specification, "ok")).Report);
    }

    [Fact]
    public void unreported_agent_result_needs_a_non_reported_outcome_and_a_reason()
    {
        Assert.Throws<ArgumentException>(() => AgentRunResult.NotReported(AgentRunOutcome.Reported, "x"));
        Assert.Throws<ArgumentException>(() => AgentRunResult.NotReported(AgentRunOutcome.TimedOut, " "));
        Assert.Null(AgentRunResult.NotReported(AgentRunOutcome.TimedOut, "30 min elapsed").Report);
    }

    [Fact]
    public void agent_run_request_requires_a_prompt()
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(AgentRole.Implementer, new AgentWorkspace("/work/trees/t1"));

        Assert.Throws<ArgumentException>(() =>
            new AgentRunRequest(new StepRunId("s1"), new AgentSessionId("session-s1"), Repo, ValidModel(), " ", policy));
        Assert.Equal(AgentRole.Implementer, new AgentRunRequest(new StepRunId("s1"), new AgentSessionId("session-s1"), Repo, ValidModel(), "go", policy).Role);
    }

    [Fact]
    public void agent_model_settings_require_model_effort_and_positive_timeout()
    {
        Assert.Throws<ArgumentException>(() => new AgentModelSettings("", "medium", TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(() => new AgentModelSettings("model", "", TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentModelSettings("model", "medium", TimeSpan.Zero));
    }

    [Fact]
    public void access_token_never_reveals_its_secret_when_printed()
    {
        var token = new GitHubAccessToken(Secret, GitHubTokenKind.AppInstallation, "installation-1", 3, null);

        Assert.DoesNotContain(Secret, token.ToString());
        Assert.DoesNotContain(Secret, GitHubTokenResult.Available(token).ToString());
        Assert.Equal(Secret, token.Value);
    }

    [Fact]
    public void unavailable_token_result_requires_a_reason()
    {
        Assert.Throws<ArgumentException>(() => GitHubTokenResult.Unavailable(""));
        Assert.False(GitHubTokenResult.Unavailable("PAT fallback disabled").IsAvailable);
    }

    [Fact]
    public void permission_sets_compare_by_content_regardless_of_order()
    {
        GitHubPermissionSet a = GitHubPermissionSet.IssuesWrite.With("contents", GitHubPermissionLevel.Read);
        GitHubPermissionSet b = GitHubPermissionSet.ContentsRead.With("issues", GitHubPermissionLevel.Write);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(GitHubPermissionSet.ContentsRead, GitHubPermissionSet.ContentsWrite);
        Assert.Equal(new GitHubTokenRequest(Repo, a), new GitHubTokenRequest(Repo, b));
        Assert.Equal("contents:read,issues:write", a.ToString());
    }

    [Fact]
    public void prerequisite_report_is_ready_with_warnings_but_not_with_failures()
    {
        var warning = new PrerequisiteCheck("gh stack", PrerequisiteStatus.Warning, "optional CLI missing");
        var failure = new PrerequisiteCheck("playwright-cli", PrerequisiteStatus.Failed, "not on PATH", "npm i -g @playwright/cli");

        Assert.True(new PrerequisiteReport([warning]).IsReady);
        Assert.False(new PrerequisiteReport([warning, failure]).IsReady);
    }

    [Fact]
    public async Task runtime_lease_releases_exactly_once()
    {
        int releases = 0;
        var lease = new CopilotRuntimeLease(
            new CopilotRuntimeKey(new CopilotAuthIdentity(CopilotAuthKind.GitHubAppInstallation, "1"), 1, null),
            () =>
            {
                releases++;
                return ValueTask.CompletedTask;
            });

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Equal(1, releases);
    }

    private static AgentModelSettings ValidModel() => new("model", "medium", TimeSpan.FromMinutes(30));
}
