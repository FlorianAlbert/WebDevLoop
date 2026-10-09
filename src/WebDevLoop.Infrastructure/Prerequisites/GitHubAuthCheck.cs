using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Checks that GitHub sign-in is configured and a user is signed in, never that the token works; secrets are never echoed.</summary>
public sealed class GitHubAuthCheck(PrerequisiteOptions options) : IPrerequisiteCheck
{
    public const string CheckName = "GitHub authentication";

    public string Name => CheckName;

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken) => Task.FromResult(Evaluate());

    private PrerequisiteCheck Evaluate()
    {
        GitHubAuthOptions auth = options.GitHubAuth;
        string[] missing =
        [
            .. string.IsNullOrWhiteSpace(auth.AppClientId) ? ["client id (WebDevLoop:GitHub:AppClientId)"] : Array.Empty<string>(),
            .. string.IsNullOrWhiteSpace(auth.AppClientSecret) ? ["client secret (WebDevLoop:GitHub:AppClientSecret)"] : Array.Empty<string>(),
        ];
        if (missing.Length > 0)
        {
            return CheckResult.Failed(
                Name,
                $"GitHub sign-in is not configured: the GitHub App's {string.Join(" and ", missing)} is missing.",
                "Configure the GitHub App's client id and a client secret (user secrets or environment variables), see the README.");
        }

        GitHubSignInStatus status = options.GitHubSignIn.Status;
        return status.IsSignedIn
            ? CheckResult.Passed(Name, $"Signed in to GitHub as {status.Login}.")
            : CheckResult.Failed(Name, "Nobody is signed in to GitHub.", "Sign in with GitHub on the GitHub page.");
    }
}
