namespace WebDevLoop.Core.Ports;

/// <summary>A GitHub issues operation failed; its message explains the lookup or permission problem.</summary>
public class GitHubIssuesException(string message, Exception? innerException = null) : Exception(message, innerException);
