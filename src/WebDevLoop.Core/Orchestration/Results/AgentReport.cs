namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Structured result an agent returns through its report tool. Constructors validate, so malformed payloads are rejected.</summary>
public abstract record AgentReport
{
    protected AgentReport(string summary)
    {
        Summary = summary ?? throw new InvalidAgentReportException("'summary' is required.");
    }

    public string Summary { get; }
}
