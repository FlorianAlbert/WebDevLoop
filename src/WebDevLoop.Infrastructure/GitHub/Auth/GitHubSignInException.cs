namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>Completing a sign-in failed (e.g. GitHub rejected the authorization code); the message is safe to show.</summary>
public sealed class GitHubSignInException(string message, Exception? innerException = null) : Exception(message, innerException);
