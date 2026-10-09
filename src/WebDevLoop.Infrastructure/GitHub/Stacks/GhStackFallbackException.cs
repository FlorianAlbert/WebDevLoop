namespace WebDevLoop.Infrastructure.GitHub.Stacks;

public sealed class GhStackFallbackException(string message) : Exception(message);
