namespace WebDevLoop.Core.Management;

/// <summary>
/// Unset <see cref="DefaultBaseBranch"/> and <see cref="CloneUrl"/> fall back to <c>main</c> and the GitHub HTTPS URL; an unset
/// <see cref="LocalPath"/> is derived from the workspace root (the app clones the repository on the server).
/// <see cref="ApproveLabelCreation"/> must be explicitly true to create a missing <c>webdevloop</c> repository label.
/// </summary>
public sealed record RegisterRepositoryCommand(string? Owner, string? Name, string? LocalPath = null, string? DefaultBaseBranch = null, string? CloneUrl = null, bool ApproveLabelCreation = false);
