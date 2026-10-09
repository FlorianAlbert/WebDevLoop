using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.GitHub.Stacks;

/// <summary>
/// Fallback for GitHub tenants where the stack REST API is unavailable: <c>gh stack link</c> works from PR/stack numbers
/// alone, needs no local stack state and never prompts. The token is passed through the environment, never as an argument.
/// </summary>
internal sealed class GhStackLinker(IGhCommandRunner runner)
{
    public async Task LinkAsync(GitHubRepoRef repo, string token, IReadOnlyList<int> targets, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> arguments = ["stack", "link", .. targets.Select(target => target.ToString())];
        var environment = new Dictionary<string, string>
        {
            ["GH_TOKEN"] = token,
            ["GH_REPO"] = repo.ToString(),
            ["GH_PROMPT_DISABLED"] = "1",
            ["NO_COLOR"] = "1",
        };

        GhCommandResult result = await runner.RunAsync(arguments, environment, cancellationToken);
        if (result.ExitCode != 0)
        {
            string error = result.StandardError.Replace(token, "***", StringComparison.Ordinal).Trim();
            throw new GhStackFallbackException($"'gh stack link' failed with exit code {result.ExitCode}: {error}");
        }
    }
}
