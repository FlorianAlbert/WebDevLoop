namespace WebDevLoop.Core.Domain;

public sealed class DependencyCycleException(string message) : InvalidOperationException(message);
