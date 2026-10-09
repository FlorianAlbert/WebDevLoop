using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Agents;

/// <summary>One agent turn: a new session (start) or a further prompt to an existing session (resume).</summary>
/// <param name="Prompt">Fully rendered prompt; placeholders are resolved before reaching the runner.</param>
/// <param name="Repository">Repository whose Copilot auth identity the session runs under.</param>
/// <param name="Policy">Authorization boundary of the session; the role is taken from it.</param>
public sealed record AgentRunRequest(
    StepRunId StepRunId,
    AgentSessionId SessionId,
    GitHubRepoRef Repository,
    AgentModelSettings Settings,
    string Prompt,
    RoleCapabilityPolicy Policy)
{
    public AgentModelSettings Settings { get; } = Settings ?? throw new ArgumentNullException(nameof(Settings));

    public string Prompt { get; } = RequirePrompt(Prompt);

    public RoleCapabilityPolicy Policy { get; } = Policy ?? throw new ArgumentNullException(nameof(Policy));

    public AgentRole Role => Policy.Role;

    private static string RequirePrompt(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, nameof(Prompt));
        return prompt;
    }
}
