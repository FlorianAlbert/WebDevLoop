#pragma warning disable GHCP001 // PermissionDecision is the SDK's (experimental) permission-handler result type.
using System.Text.Json;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Infrastructure.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;
using WebDevLoop.Infrastructure.Copilot.Sdk;

namespace WebDevLoop.Infrastructure.Tests.Copilot;

/// <summary>Maps the SDK-independent session spec onto the real GitHub.Copilot.SDK types, without starting a runtime.</summary>
public sealed class SdkMappingTests
{
    private const string Worktree = "/work/trees/t1";
    private const string Notes = "/work/notes/run1";
    private const string SkillsRoot = "/app/skills";

    private readonly List<CopilotSessionEvent> _events = [];
    private readonly List<JsonElement> _reports = [];

    [Fact]
    public void session_config_carries_workspace_model_skills_and_role_tool_filters()
    {
        SessionConfig config = SdkSessionConfigFactory.CreateSession(Spec(AgentRole.Implementer));

        Assert.Equal("session-1", config.SessionId);
        Assert.Equal(Worktree, config.WorkingDirectory);
        Assert.Equal("gpt-test", config.Model);
        Assert.Equal("high", config.ReasoningEffort);
        Assert.Equal([SkillsRoot], config.SkillDirectories);
        Assert.True(config.EnableSkills);
        Assert.Contains("builtin:bash", config.AvailableTools!);
        Assert.Contains("builtin:edit", config.AvailableTools!);
        Assert.Contains($"custom:{AgentReportTools.Implementation}", config.AvailableTools!);
        Assert.DoesNotContain(config.AvailableTools!, tool => tool.StartsWith("mcp:", StringComparison.Ordinal));
        Assert.Contains("mcp:*", config.ExcludedTools!);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(config.Tools!));
        Assert.Equal(AgentReportTools.Implementation, tool.Name);
        Assert.Equal(true, tool.AdditionalProperties["is_terminal"]);
        Assert.Equal(true, tool.AdditionalProperties["skip_permission"]);
        Assert.Equal("object", tool.JsonSchema.GetProperty("type").GetString());
        Assert.Equal("ghu_session", config.GitHubToken);
        Assert.Null(config.GitHubTokenProvider);
    }

    [Fact]
    public void resume_config_restores_the_same_workspace_tools_and_handlers()
    {
        ResumeSessionConfig config = SdkSessionConfigFactory.CreateResume(Spec(AgentRole.ReviewerSpecification));

        Assert.Equal(Worktree, config.WorkingDirectory);
        Assert.Equal([SkillsRoot], config.SkillDirectories);
        Assert.Contains($"custom:{AgentReportTools.Review}", config.AvailableTools!);
        Assert.DoesNotContain("builtin:edit", config.AvailableTools!);
        Assert.Equal(AgentReportTools.Review, Assert.IsAssignableFrom<AIFunction>(Assert.Single(config.Tools!)).Name);
        Assert.NotNull(config.OnPermissionRequest);
        Assert.NotNull(config.OnEvent);
        Assert.True(config.AllowTranscriptRecovery);
    }

    [Fact]
    public async Task sdk_permission_requests_are_decided_by_the_role_policy()
    {
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>> handler =
            SdkSessionConfigFactory.CreateSession(Spec(AgentRole.Implementer)).OnPermissionRequest!;
        var invocation = new PermissionInvocation { SessionId = "session-1" };

        PermissionDecision push = await handler(ShellRequest("git push origin HEAD"), invocation);
        PermissionDecision test = await handler(ShellRequest("dotnet test"), invocation);
        PermissionDecision write = await handler(WriteRequest($"{Worktree}/src/a.cs"), invocation);
        PermissionDecision writeNotes = await handler(WriteRequest($"{Notes}/README.md"), invocation);
        PermissionDecision read = await handler(new PermissionRequestRead { Path = $"{Notes}/README.md", Intention = "read" }, invocation);
        PermissionDecision mcp = await handler(McpRequest("create_pull_request"), invocation);
        PermissionDecision unknown = await handler(new PermissionRequest { Kind = "memory" }, invocation);

        Assert.Contains("git push", Assert.IsType<PermissionDecisionReject>(push).Feedback);
        Assert.IsType<PermissionDecisionApproveOnce>(test);
        Assert.IsType<PermissionDecisionApproveOnce>(write);
        Assert.IsType<PermissionDecisionReject>(writeNotes);
        Assert.IsType<PermissionDecisionApproveOnce>(read);
        Assert.IsType<PermissionDecisionReject>(mcp);
        Assert.IsType<PermissionDecisionReject>(unknown);
    }

    [Fact]
    public async Task report_function_hands_the_raw_arguments_to_the_report_tool()
    {
        var tool = (AIFunction)SdkSessionConfigFactory.CreateSession(Spec(AgentRole.Explorer)).Tools!.Single();

        object? result = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["status"] = "completed",
            ["summary"] = "Explored.",
            ["notes_files"] = JsonSerializer.SerializeToElement(new[] { "README.md" }),
        }, TestContext.Current.CancellationToken);

        Assert.Equal("recorded", result?.ToString());
        JsonElement arguments = Assert.Single(_reports);
        Assert.Equal("completed", arguments.GetProperty("status").GetString());
        Assert.Equal("README.md", arguments.GetProperty("notes_files")[0].GetString());
    }

    [Fact]
    public void non_expiring_user_tokens_are_passed_per_session()
    {
        SessionConfig config = SdkSessionConfigFactory.CreateSession(Spec(AgentRole.Tester, CopilotSessionAuth.StaticToken("ghu_static")));

        Assert.Equal("ghu_static", config.GitHubToken);
        Assert.Null(config.GitHubTokenProvider);
    }

    [Fact]
    public async Task rotating_user_tokens_are_served_through_the_sdk_token_provider()
    {
        int calls = 0;
        CopilotSessionAuth auth = CopilotSessionAuth.RotatingToken(_ =>
        {
            calls++;
            return Task.FromResult(new CopilotUserToken($"ghu_{calls}", TimeSpan.FromMinutes(30)));
        });
        SessionConfig config = SdkSessionConfigFactory.CreateSession(Spec(AgentRole.Implementer, auth));

        GitHubTokenProviderResult first = await config.GitHubTokenProvider!(new GitHubTokenProviderArgs { Host = "github.com", Reason = GitHubTokenRequestReason.Initial });
        GitHubTokenProviderResult refreshed = await config.GitHubTokenProvider!(new GitHubTokenProviderArgs { Host = "github.com", Reason = GitHubTokenRequestReason.Refresh });

        Assert.Null(config.GitHubToken);
        Assert.Equal("ghu_1", first.Token!.AccessToken);
        Assert.Equal(1800, first.Token.ExpiresIn);
        Assert.Equal("ghu_2", refreshed.Token!.AccessToken);
    }

    [Fact]
    public void sdk_events_are_forwarded_as_session_events()
    {
        Action<SessionEvent> onEvent = SdkSessionConfigFactory.CreateSession(Spec(AgentRole.Implementer)).OnEvent!;

        onEvent(new AssistantMessageEvent { Data = new AssistantMessageData { Content = "Working on it.", MessageId = "m1" } });
        onEvent(new ToolExecutionStartEvent { Data = new ToolExecutionStartData { ToolName = "bash", ToolCallId = "c1" } });
        onEvent(new SessionErrorEvent { Data = new SessionErrorData { ErrorType = "authentication", Message = "Bad credentials", StatusCode = 401 } });
        onEvent(new SessionErrorEvent { Data = new SessionErrorData { ErrorType = "model", Message = "Overloaded", StatusCode = 503 } });
        onEvent(new SessionIdleEvent { Data = new SessionIdleData() });

        Assert.Equal(
            [
                new CopilotSessionEvent(CopilotSessionEventKind.AssistantMessage, "Working on it."),
                new CopilotSessionEvent(CopilotSessionEventKind.ToolStarted, "bash"),
                new CopilotSessionEvent(CopilotSessionEventKind.Error, "Bad credentials", IsAuthenticationFailure: true),
                new CopilotSessionEvent(CopilotSessionEventKind.Error, "Overloaded"),
                new CopilotSessionEvent(CopilotSessionEventKind.Idle, string.Empty),
            ],
            _events);
    }

    [Fact]
    public void client_options_launch_an_isolated_runtime_with_the_given_environment()
    {
        var environment = new Dictionary<string, string> { ["PATH"] = "/usr/bin" };
        var launch = new CopilotRuntimeLaunch(
            new CopilotRuntimeKey(new CopilotAuthIdentity("octocat"), 3, null),
            "/data/copilot",
            "/usr/local/bin/copilot",
            environment);

        CopilotClientOptions options = SdkClientOptionsFactory.Create(launch);

        Assert.Equal("/data/copilot", options.BaseDirectory);
        Assert.Same(environment, options.Environment);
        Assert.Equal(false, options.UseLoggedInUser);
        Assert.Null(options.GitHubToken);
        Assert.Equal("/usr/local/bin/copilot", Assert.IsType<StdioRuntimeConnection>(options.Connection).Path);
    }

    [Theory]
    [InlineData("Request failed: 401 Unauthorized", true)]
    [InlineData("Not authenticated. Run copilot login.", true)]
    [InlineData("Bad credentials", true)]
    [InlineData("The runtime process exited unexpectedly.", false)]
    public void authentication_failures_are_recognised(string message, bool expected)
    {
        Assert.Equal(expected, SdkFailures.IsAuthenticationFailure(new InvalidOperationException(message)));
    }

    private CopilotSessionSpec Spec(AgentRole role, CopilotSessionAuth? auth = null)
    {
        RoleCapabilityPolicy rolePolicy = RoleCapabilityPolicies.For(role, new AgentWorkspace(Worktree, Notes));
        var policy = new AgentSessionPolicy(rolePolicy, SkillsRoot);
        return new CopilotSessionSpec(
            new AgentSessionId("session-1"),
            Worktree,
            new AgentModelSettings("gpt-test", "high", TimeSpan.FromMinutes(5)),
            [SkillsRoot],
            policy.Tools,
            new CopilotReportTool(
                rolePolicy.ReportToolName,
                "Report.",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }),
                arguments =>
                {
                    _reports.Add(arguments);
                    return "recorded";
                }),
            policy.Authorize,
            auth ?? CopilotSessionAuth.StaticToken("ghu_session"),
            _events.Add);
    }

    private static PermissionRequestShell ShellRequest(string command) => new()
    {
        FullCommandText = command,
        Intention = "run",
        CanOfferSessionApproval = false,
        Commands = [],
        CommandSegments = [],
        HasWriteFileRedirection = false,
        PossiblePaths = [],
        PossibleUrls = [],
    };

    private static PermissionRequestWrite WriteRequest(string path) => new()
    {
        FileName = path,
        Intention = "edit",
        CanOfferSessionApproval = false,
        Diff = string.Empty,
    };

    private static PermissionRequestMcp McpRequest(string tool) => new()
    {
        ServerName = "github-mcp-server",
        ToolName = tool,
        ToolTitle = tool,
        ReadOnly = false,
    };
}
