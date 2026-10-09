using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

public static class CopilotAuthIdentityExtensions
{
    /// <summary>The Copilot runtime-pool identity a token authenticates; its <see cref="GitHubAccessToken.Generation"/> completes the runtime key.</summary>
    public static CopilotAuthIdentity ToCopilotIdentity(this GitHubAccessToken token) => new(token.IdentityId);
}
