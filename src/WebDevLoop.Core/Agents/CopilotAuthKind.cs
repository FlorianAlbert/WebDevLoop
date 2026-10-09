namespace WebDevLoop.Core.Agents;

public enum CopilotAuthKind
{
    /// <summary>App installation token supplied through the runtime environment; rotating it requires a new runtime.</summary>
    GitHubAppInstallation,

    /// <summary>User/PAT token supplied per session through a token-provider callback.</summary>
    UserToken,
}
