namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>Copilot credentials were unavailable or rejected; the runner retries once on a replaced runtime.</summary>
public sealed class CopilotAuthenticationException(string message, Exception? innerException = null) : Exception(message, innerException);
