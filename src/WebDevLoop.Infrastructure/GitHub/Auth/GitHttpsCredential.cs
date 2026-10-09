namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>Username/password pair for Git over HTTPS. <see cref="ToString"/> never reveals the secret.</summary>
public sealed class GitHttpsCredential(string username, string password)
{
    public string Username { get; } = username;

    public string Password { get; } = password;

    public override string ToString() => $"{Username}:***";
}
