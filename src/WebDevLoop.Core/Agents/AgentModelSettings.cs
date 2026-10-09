namespace WebDevLoop.Core.Agents;

/// <summary>Resolved per-role model settings handed to the agent runner.</summary>
public sealed record AgentModelSettings
{
    public AgentModelSettings(string model, string reasoningEffort, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasoningEffort);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        Model = model;
        ReasoningEffort = reasoningEffort;
        Timeout = timeout;
    }

    public string Model { get; }

    public string ReasoningEffort { get; }

    public TimeSpan Timeout { get; }
}
