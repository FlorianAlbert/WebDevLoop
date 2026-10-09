namespace WebDevLoop.Core.Management;

/// <summary>Unset <see cref="DefaultBaseBranch"/> and <see cref="CloneUrl"/> fall back to <c>main</c> and the GitHub HTTPS URL.</summary>
public sealed record RegisterRepositoryCommand(string? Owner, string? Name, string? LocalPath, string? DefaultBaseBranch = null, string? CloneUrl = null);
