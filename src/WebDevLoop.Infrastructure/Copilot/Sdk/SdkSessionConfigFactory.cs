#pragma warning disable GHCP001 // Session token providers and PermissionDecision are marked experimental in the SDK; they are the documented auth/permission hooks.
using GitHub.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal static class SdkSessionConfigFactory
{
    public static SessionConfig CreateSession(CopilotSessionSpec spec)
    {
        var config = new SessionConfig { SessionId = spec.SessionId.Value };
        Apply(config, spec);
        return config;
    }

    /// <summary>Resuming re-registers the same working directory, tools, policy handlers, and credentials.</summary>
    public static ResumeSessionConfig CreateResume(CopilotSessionSpec spec)
    {
        var config = new ResumeSessionConfig { AllowTranscriptRecovery = true };
        Apply(config, spec);
        return config;
    }

    private static void Apply(SessionConfigBase config, CopilotSessionSpec spec)
    {
        config.WorkingDirectory = spec.WorkingDirectory;
        config.Model = spec.Settings.Model;
        config.ReasoningEffort = spec.Settings.ReasoningEffort;
        config.SkillDirectories = [.. spec.SkillDirectories];
        config.EnableSkills = true;
        config.AvailableTools = AvailableTools(spec.Tools);
        config.ExcludedTools = new ToolSet().AddMcp("*");
        config.Tools = [new SdkReportFunction(spec.ReportTool)];
        config.OnPermissionRequest = SdkPermissions.CreateHandler(spec.AuthorizePermission);
        config.OnEvent = sessionEvent =>
        {
            if (SdkEvents.Map(sessionEvent) is { } mapped)
            {
                spec.OnEvent(mapped);
            }
        };
        config.GitHubToken = spec.Auth.GitHubToken;
        config.GitHubTokenProvider = spec.Auth.TokenProvider is { } provider ? args => ProvideTokenAsync(provider) : null;
    }

    private static ToolSet AvailableTools(CopilotToolSelection tools)
    {
        ToolSet available = new ToolSet().AddBuiltIn(tools.BuiltInTools);
        foreach (string customTool in tools.CustomTools)
        {
            available.AddCustom(customTool);
        }

        return available;
    }

    private static async Task<GitHubTokenProviderResult> ProvideTokenAsync(Func<CancellationToken, Task<CopilotUserToken>> provider)
    {
        CopilotUserToken token = await provider(CancellationToken.None);
        return GitHubTokenProviderResult.FromToken(new GitHubToken
        {
            AccessToken = token.Value,
            ExpiresIn = (long)token.ExpiresIn.TotalSeconds,
        });
    }
}
