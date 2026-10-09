namespace WebDevLoop.Core.Agents;

public enum AgentRunOutcome
{
    /// <summary>The agent called its report tool with a valid report.</summary>
    Reported,

    /// <summary>The turn ended without calling the report tool.</summary>
    MissingReport,

    /// <summary>The report tool was called with a payload that failed domain validation.</summary>
    InvalidReport,

    TimedOut,
    Cancelled,
    AuthenticationFailed,
    Failed,
}
