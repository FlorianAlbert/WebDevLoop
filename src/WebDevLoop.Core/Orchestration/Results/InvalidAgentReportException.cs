namespace WebDevLoop.Core.Orchestration.Results;

public sealed class InvalidAgentReportException(string message) : Exception(message);
