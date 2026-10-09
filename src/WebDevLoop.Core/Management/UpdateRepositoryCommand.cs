namespace WebDevLoop.Core.Management;

/// <summary>Patch semantics: only the supplied values change.</summary>
public sealed record UpdateRepositoryCommand(string? DefaultBaseBranch = null, string? CloneUrl = null, string? LocalPath = null, bool? IsEnabled = null);
