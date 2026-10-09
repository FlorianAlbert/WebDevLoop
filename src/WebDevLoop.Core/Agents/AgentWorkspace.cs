namespace WebDevLoop.Core.Agents;

/// <param name="WorkingDirectory">Worktree, read-only checkout, or tester lease workspace the session runs in.</param>
/// <param name="NotesDirectory">App-allocated exploration notes directory outside the repository, shared with later agents.</param>
public sealed record AgentWorkspace(string WorkingDirectory, string? NotesDirectory = null);
