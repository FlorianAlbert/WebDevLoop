namespace WebDevLoop.Infrastructure.GitHub.Auth;

public sealed class GitCredentialUnavailableException(string reason) : Exception(reason);
