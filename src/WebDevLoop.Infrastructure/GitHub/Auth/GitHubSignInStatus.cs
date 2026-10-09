namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <param name="IsConfigured">The App's client id and client secret are configured, so users can sign in.</param>
/// <param name="SignInExpiresAt">When the user must sign in again (the refresh token expires); null when it never does.</param>
public sealed record GitHubSignInStatus(bool IsConfigured, string? Login, string? AvatarUrl, DateTimeOffset? SignInExpiresAt)
{
    public bool IsSignedIn => Login is not null;
}

/// <summary>Who is signed in to GitHub; <see cref="Changed"/> fires after every sign-in, sign-out and expired sign-in.</summary>
public interface IGitHubSignInState
{
    GitHubSignInStatus Status { get; }

    event Action? Changed;
}
