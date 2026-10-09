using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

public static class CopilotAuthIdentityExtensions
{
    /// <summary>The Copilot runtime-pool identity a token authenticates; its <see cref="GitHubAccessToken.Generation"/> completes the runtime key.</summary>
    public static CopilotAuthIdentity ToCopilotIdentity(this GitHubAccessToken token) => new(
        token.Kind switch
        {
            GitHubTokenKind.AppInstallation => CopilotAuthKind.GitHubAppInstallation,
            GitHubTokenKind.UserToken => CopilotAuthKind.UserToken,
            _ => throw new ArgumentOutOfRangeException(nameof(token), token.Kind, null),
        },
        token.IdentityId);
}
