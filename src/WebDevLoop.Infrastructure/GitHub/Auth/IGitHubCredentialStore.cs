namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>Persists the signed-in user's credentials across restarts; implementations must protect them at rest.</summary>
public interface IGitHubCredentialStore
{
    /// <returns>Null when nobody signed in, or the stored credentials can no longer be read (e.g. lost encryption keys).</returns>
    GitHubUserCredentials? Load();

    void Save(GitHubUserCredentials credentials);

    void Clear();
}
