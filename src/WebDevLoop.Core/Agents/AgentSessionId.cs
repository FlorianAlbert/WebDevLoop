namespace WebDevLoop.Core.Agents;

/// <summary>Copilot session id chosen by the app before starting a session, so it is persisted before any agent work happens.</summary>
public readonly record struct AgentSessionId
{
    public AgentSessionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
