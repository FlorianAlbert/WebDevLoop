using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Checks that credentials are configured, never that they are valid; secrets are never echoed.</summary>
public sealed class GitHubAuthCheck(PrerequisiteOptions options) : IPrerequisiteCheck
{
    public const string CheckName = "GitHub authentication";

    private const string ConfigureRemediation =
        "Configure a GitHub App (client id and private key), or enable the PAT fallback together with a fine-grained token.";

    public string Name => CheckName;

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken) => Task.FromResult(Evaluate());

    private PrerequisiteCheck Evaluate()
    {
        var auth = options.GitHubAuth;
        bool hasClientId = !string.IsNullOrWhiteSpace(auth.AppClientId);
        bool hasPrivateKey = !string.IsNullOrWhiteSpace(auth.AppPrivateKeyPem);
        bool hasToken = !string.IsNullOrWhiteSpace(auth.UserToken);

        if (hasClientId != hasPrivateKey)
        {
            string missing = hasClientId ? "private key" : "client id";
            return CheckResult.Failed(Name, $"The GitHub App is half configured: the {missing} is missing.", ConfigureRemediation);
        }

        if (auth.IsAppConfigured)
        {
            return auth.PatFallbackEnabled && !hasToken
                ? CheckResult.Warning(
                    Name,
                    "GitHub App configured, but the PAT fallback is enabled without a token.",
                    "Provide a token or disable the PAT fallback.")
                : CheckResult.Passed(Name, "GitHub App credentials are configured.");
        }

        return auth.PatFallbackEnabled && hasToken
            ? CheckResult.Warning(
                Name,
                "No GitHub App configured: only the PAT fallback is available, which acts with a user's broader permissions.",
                "Configure a GitHub App for least-privilege, per-repository installation tokens.")
            : CheckResult.Failed(Name, "No GitHub credentials are configured.", ConfigureRemediation);
    }
}
