namespace WebDevLoop.Infrastructure.GitHub.Pulls;

public sealed class GitHubTokenUnavailableException(string reason) : Exception($"No GitHub token is available: {reason}")
{
    public string Reason { get; } = reason;
}
