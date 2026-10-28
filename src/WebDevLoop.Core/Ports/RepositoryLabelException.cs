namespace WebDevLoop.Core.Ports;

/// <summary>A label operation could not be verified; callers must defer new work and allow a retry.</summary>
public sealed class RepositoryLabelException(string message, Exception? innerException = null) : Exception(message, innerException);
