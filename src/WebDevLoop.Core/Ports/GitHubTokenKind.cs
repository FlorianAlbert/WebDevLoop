namespace WebDevLoop.Core.Ports;

public enum GitHubTokenKind
{
    AppInstallation,

    /// <summary>Fine-grained PAT or user access token, used only as a configured fallback.</summary>
    UserToken,
}
