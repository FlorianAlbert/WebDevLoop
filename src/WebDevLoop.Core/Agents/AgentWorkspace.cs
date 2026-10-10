namespace WebDevLoop.Core.Agents;

/// <param name="WorkingDirectory">Worktree, read-only checkout, or tester lease workspace the session runs in.</param>
/// <param name="NotesDirectory">App-allocated exploration notes directory outside the repository, shared with later agents.</param>
/// <param name="AdditionalWorkDirectories">Further directories the session may modify besides <paramref name="WorkingDirectory"/> (the troubleshooter's integration checkout and backup folder).</param>
/// <param name="ProtectedBranches">Branches the session must not check out, switch to or rebase, because WebDevLoop owns them (the integration and trunk branches).</param>
public sealed record AgentWorkspace(
    string WorkingDirectory,
    string? NotesDirectory = null,
    IReadOnlyList<string>? AdditionalWorkDirectories = null,
    IReadOnlyList<string>? ProtectedBranches = null);
