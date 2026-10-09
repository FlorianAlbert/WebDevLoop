using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;
using WebDevLoop.Infrastructure.Skills;
using WebDevLoop.Infrastructure.Tests.Copilot.Fakes;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Copilot;

public sealed class CopilotAgentRunnerTests : IAsyncDisposable
{
    private const string Worktree = "/work/trees/t1";
    private const string Notes = "/work/notes/run1";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string ImplementationReportJson = $$"""
        { "status": "completed", "head_commit_sha": "{{Sha}}", "summary": "Done.", "tests": [], "addressed_findings": [], "follow_ups": [] }
        """;

    private static readonly GitHubRepoRef Repo = new("octo", "app");
    private static readonly StepRunId Step = new("step-1");
    private static readonly AgentSessionId Session = new("session-1");

    private static readonly Dictionary<string, string> HostEnvironment = new()
    {
        ["PATH"] = "/usr/bin",
        ["HOME"] = "/home/app",
        ["GH_TOKEN"] = "gho_host",
        ["GITHUB_TOKEN"] = "ghs_host_write",
        ["COPILOT_GITHUB_TOKEN"] = "ghs_host_copilot",
    };

    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeCopilotRuntimeFactory _factory = new();
    private readonly RecordingLogSink _logs = new();
    private CopilotTokenProviderFake _tokens;
    private CopilotRuntimePool _pool;
    private CopilotAgentRunner _runner;

    public CopilotAgentRunnerTests()
    {
        _tokens = new CopilotTokenProviderFake(_clock);
        (_pool, _runner) = CreateRunner(_tokens);
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SkillsRoot => Path.Combine(AppContext.BaseDirectory, BundledSkillsOptions.DefaultDirectoryName);

    [Fact]
    public async Task start_creates_the_session_sends_the_prompt_and_returns_the_validated_report()
    {
        _factory.OnSend = turn =>
        {
            turn.Raise(CopilotSessionEventKind.AssistantMessage, "Implementing.");
            turn.Report(ImplementationReportJson);
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        Assert.Equal(AgentRunOutcome.Reported, result.Outcome);
        Assert.Equal(new CommitSha(Sha), Assert.IsType<ImplementationReport>(result.Report).HeadCommitSha);
        FakeCopilotRuntime runtime = Assert.Single(_factory.Runtimes);
        CopilotSessionSpec spec = Assert.Single(runtime.Created);
        Assert.Equal(Session, spec.SessionId);
        Assert.Equal(Worktree, spec.WorkingDirectory);
        Assert.Equal("gpt-test", spec.Settings.Model);
        Assert.Equal([SkillsRoot], spec.SkillDirectories);
        Assert.Equal(AgentReportTools.Implementation, spec.ReportTool.Name);
        Assert.Equal(["Implement ticket #12."], runtime.Sessions[0].Prompts);
        Assert.True(runtime.Sessions[0].IsDisposed);
        Assert.Contains(_logs.Entries, entry => entry is { Kind: AgentLogKind.Assistant, Text: "Implementing." } && entry.StepRunId == Step);
    }

    [Fact]
    public async Task implementer_session_denies_git_push_and_github_tools_while_allowing_local_edit_test_and_report()
    {
        _factory.OnSend = turn =>
        {
            Assert.False(turn.Spec.AuthorizePermission(new AgentPermissionRequest(AgentPermissionKind.Shell, "git push origin HEAD", "shell")).IsApproved);
            Assert.False(turn.Spec.AuthorizePermission(new AgentPermissionRequest(AgentPermissionKind.Mcp, "github-mcp-server/create_issue", "mcp")).IsApproved);
            Assert.True(turn.Spec.AuthorizePermission(new AgentPermissionRequest(AgentPermissionKind.Shell, "dotnet test", "shell")).IsApproved);
            Assert.True(turn.Spec.AuthorizePermission(new AgentPermissionRequest(AgentPermissionKind.Write, $"{Worktree}/src/a.cs", "write")).IsApproved);
            turn.Report(ImplementationReportJson);
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        Assert.Equal(AgentRunOutcome.Reported, result.Outcome);
        CopilotSessionSpec spec = _factory.Runtimes[0].Created[0];
        Assert.Contains("edit", spec.Tools.BuiltInTools);
        Assert.Equal([AgentReportTools.Implementation], spec.Tools.CustomTools);
        Assert.Contains(_logs.Entries, entry => entry.Kind == AgentLogKind.PermissionDenied && entry.Text.Contains("git push"));
    }

    [Theory]
    [InlineData(AgentRole.ReviewerCodingStandards)]
    [InlineData(AgentRole.ReviewerSpecification)]
    [InlineData(AgentRole.Tester)]
    public async Task reviewer_and_tester_sessions_receive_no_github_write_token_and_scrubbed_credentials(AgentRole role)
    {
        await _runner.StartAsync(Request(role), Ct);

        IReadOnlyDictionary<string, string> environment = Assert.Single(_factory.Runtimes).Launch.Environment;
        Assert.DoesNotContain(environment.Keys, name => name is "GH_TOKEN" or "GITHUB_TOKEN");
        Assert.Equal("ghs_gen1", environment["COPILOT_GITHUB_TOKEN"]);
        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.Equal("0", environment["GIT_TERMINAL_PROMPT"]);
        Assert.All(_tokens.Requests, request => Assert.Equal(GitHubPermissionSet.CopilotRequests, request.Permissions));
        CopilotSessionSpec spec = _factory.Runtimes[0].Created[0];
        Assert.Null(spec.Auth.GitHubToken);
        Assert.Null(spec.Auth.TokenProvider);
    }

    [Fact]
    public async Task a_turn_ending_without_a_report_is_missing_report()
    {
        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Explorer), Ct);

        Assert.Equal(AgentRunOutcome.MissingReport, result.Outcome);
        Assert.Contains(AgentReportTools.Exploration, result.FailureReason);
    }

    [Fact]
    public async Task a_report_that_fails_validation_is_an_invalid_report()
    {
        string toolResult = string.Empty;
        _factory.OnSend = turn =>
        {
            toolResult = turn.Report("""{ "axis": "specification", "verdict": "issues_found", "summary": "s", "findings": [] }""");
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.ReviewerSpecification), Ct);

        Assert.Equal(AgentRunOutcome.InvalidReport, result.Outcome);
        Assert.Contains("findings", result.FailureReason);
        Assert.Contains("rejected", toolResult);
    }

    [Fact]
    public async Task a_session_error_fails_the_turn()
    {
        _factory.OnSend = turn =>
        {
            turn.Raise(CopilotSessionEventKind.Error, "Model overloaded");
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        Assert.Contains("Model overloaded", result.FailureReason);
        Assert.Contains(_logs.Entries, entry => entry is { Kind: AgentLogKind.Error, Text: "Model overloaded" });
    }

    [Fact]
    public async Task a_turn_exceeding_the_role_timeout_is_aborted()
    {
        _factory.OnSend = _ => Task.CompletedTask;

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer, timeout: TimeSpan.FromMilliseconds(50)), Ct);

        Assert.Equal(AgentRunOutcome.TimedOut, result.Outcome);
        Assert.Equal(1, _factory.Runtimes[0].Sessions[0].AbortCount);
    }

    [Fact]
    public async Task cancelling_aborts_the_turn()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _factory.OnSend = _ =>
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), cancellation.Token);

        Assert.Equal(AgentRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, _factory.Runtimes[0].Sessions[0].AbortCount);
    }

    [Fact]
    public async Task abort_reaches_the_running_session_and_ignores_unknown_sessions()
    {
        Task<AgentRunResult>? run = null;
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.OnSend = _ =>
        {
            sent.SetResult();
            return Task.CompletedTask;
        };

        run = _runner.StartAsync(Request(AgentRole.Implementer, timeout: TimeSpan.FromSeconds(5)), Ct);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await _runner.AbortAsync(Session, Ct);
        await _runner.AbortAsync(new AgentSessionId("unknown"), Ct);
        FakeCopilotSession session = _factory.Runtimes[0].Sessions[0];
        session.Spec.OnEvent(new CopilotSessionEvent(CopilotSessionEventKind.Idle, string.Empty));
        await run;

        Assert.Equal(1, session.AbortCount);
    }

    [Fact]
    public async Task resume_reopens_the_persisted_session_with_the_same_worktree_and_tools()
    {
        _factory.OnSend = turn =>
        {
            turn.Report(ImplementationReportJson);
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.ResumeAsync(Request(AgentRole.Implementer, prompt: "Fix the findings."), Ct);

        Assert.Equal(AgentRunOutcome.Reported, result.Outcome);
        FakeCopilotRuntime runtime = Assert.Single(_factory.Runtimes);
        Assert.Empty(runtime.Created);
        CopilotSessionSpec spec = Assert.Single(runtime.Resumed);
        Assert.Equal(Session, spec.SessionId);
        Assert.Equal(Worktree, spec.WorkingDirectory);
        Assert.Equal(AgentReportTools.Implementation, spec.ReportTool.Name);
        Assert.Equal(["Fix the findings."], runtime.Sessions[0].Prompts);
    }

    [Fact]
    public async Task crossing_app_token_expiry_drains_the_old_runtime_and_resumes_the_persisted_session_on_a_new_runtime()
    {
        _factory.OnSend = turn =>
        {
            turn.Report(ImplementationReportJson);
            return Task.CompletedTask;
        };
        await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        _clock.Advance(TimeSpan.FromMinutes(58));
        AgentRunResult resumed = await _runner.ResumeAsync(Request(AgentRole.Implementer, prompt: "Fix the findings."), Ct);

        Assert.Equal(AgentRunOutcome.Reported, resumed.Outcome);
        Assert.Equal(2, _factory.Runtimes.Count);
        Assert.True(_factory.Runtimes[0].IsDisposed);
        FakeCopilotRuntime replacement = _factory.Runtimes[1];
        Assert.Equal("ghs_gen2", replacement.Launch.Environment["COPILOT_GITHUB_TOKEN"]);
        Assert.Equal(_factory.Runtimes[0].Launch.BaseDirectory, replacement.Launch.BaseDirectory);
        CopilotSessionSpec created = _factory.Runtimes[0].Created[0];
        CopilotSessionSpec spec = Assert.Single(replacement.Resumed);
        Assert.Equal(created.SessionId, spec.SessionId);
        Assert.Equal(created.WorkingDirectory, spec.WorkingDirectory);
        Assert.Equal(created.SkillDirectories, spec.SkillDirectories);
        Assert.Equal(created.ReportTool.Name, spec.ReportTool.Name);
        Assert.Equal(created.Tools.BuiltInTools, spec.Tools.BuiltInTools);
    }

    [Fact]
    public async Task an_authentication_failure_retries_once_through_runtime_replacement()
    {
        _factory.OnSend = turn =>
        {
            if (turn.Runtime.Index == 0)
            {
                turn.Raise(CopilotSessionEventKind.Error, "Bad credentials", isAuthenticationFailure: true);
            }
            else
            {
                turn.Report(ImplementationReportJson);
            }

            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        Assert.Equal(AgentRunOutcome.Reported, result.Outcome);
        Assert.Equal(2, _factory.Runtimes.Count);
        Assert.True(_factory.Runtimes[0].IsDisposed);
        Assert.Equal(Session, Assert.Single(_factory.Runtimes[1].Resumed).SessionId);
        Assert.Equal(["Implement ticket #12."], _factory.Runtimes[1].Sessions[0].Prompts);
    }

    [Fact]
    public async Task an_authentication_failure_while_creating_the_session_recreates_it_on_the_new_runtime()
    {
        _factory.OnOpenSession = index => index == 0 ? new CopilotAuthenticationException("401 Unauthorized") : null;
        _factory.OnSend = turn =>
        {
            turn.Report(ImplementationReportJson);
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        Assert.Equal(AgentRunOutcome.Reported, result.Outcome);
        Assert.Single(_factory.Runtimes[1].Created);
        Assert.Empty(_factory.Runtimes[1].Resumed);
    }

    [Fact]
    public async Task a_second_authentication_failure_is_reported_without_further_retries()
    {
        _factory.OnSend = turn =>
        {
            turn.Raise(CopilotSessionEventKind.Error, "Bad credentials", isAuthenticationFailure: true);
            return Task.CompletedTask;
        };

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        Assert.Equal(AgentRunOutcome.AuthenticationFailed, result.Outcome);
        Assert.Equal(2, _factory.Runtimes.Count);
    }

    [Fact]
    public async Task unavailable_copilot_credentials_fail_authentication_without_starting_a_runtime()
    {
        _tokens.UnavailableReason = "PAT fallback is disabled.";

        AgentRunResult result = await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        Assert.Equal(AgentRunOutcome.AuthenticationFailed, result.Outcome);
        Assert.Contains("PAT fallback is disabled.", result.FailureReason);
        Assert.Empty(_factory.Runtimes);
    }

    [Fact]
    public async Task rotatable_user_token_sessions_use_the_token_provider_callback()
    {
        await _pool.DisposeAsync();
        _tokens = new CopilotTokenProviderFake(_clock, GitHubTokenKind.UserToken) { UserTokensRotate = true };
        (_pool, _runner) = CreateRunner(_tokens);

        await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        CopilotSessionSpec spec = _factory.Runtimes[0].Created[0];
        Assert.Null(spec.Auth.GitHubToken);
        Assert.NotNull(spec.Auth.TokenProvider);
        Assert.DoesNotContain("COPILOT_GITHUB_TOKEN", _factory.Runtimes[0].Launch.Environment.Keys);
        _clock.Advance(TimeSpan.FromMinutes(58));
        CopilotUserToken refreshed = await spec.Auth.TokenProvider(Ct);
        Assert.Equal("ghu_gen2", refreshed.Value);
        Assert.Equal(CopilotTokenProviderFake.Lifetime, refreshed.ExpiresIn);
    }

    [Fact]
    public async Task non_expiring_user_tokens_are_passed_to_the_session_directly()
    {
        await _pool.DisposeAsync();
        _tokens = new CopilotTokenProviderFake(_clock, GitHubTokenKind.UserToken);
        (_pool, _runner) = CreateRunner(_tokens);

        await _runner.StartAsync(Request(AgentRole.Implementer), Ct);

        CopilotSessionSpec spec = _factory.Runtimes[0].Created[0];
        Assert.Equal("ghu_gen1", spec.Auth.GitHubToken);
        Assert.Null(spec.Auth.TokenProvider);
    }

    private (CopilotRuntimePool Pool, CopilotAgentRunner Runner) CreateRunner(ITokenProvider tokens)
    {
        var options = new CopilotRuntimeOptions { BaseDirectory = "/data/copilot", InheritedEnvironment = () => HostEnvironment };
        var pool = new CopilotRuntimePool(_factory, tokens, _clock, options);
        var runner = new CopilotAgentRunner(pool, new BundledSkillsCatalog(new BundledSkillsOptions()), _logs, _clock, options);
        return (pool, runner);
    }

    private static AgentRunRequest Request(AgentRole role, TimeSpan? timeout = null, string prompt = "Implement ticket #12.") => new(
        Step,
        Session,
        Repo,
        new AgentModelSettings("gpt-test", "high", timeout ?? TimeSpan.FromSeconds(10)),
        prompt,
        RoleCapabilityPolicies.For(role, new AgentWorkspace(Worktree, Notes)));
}
