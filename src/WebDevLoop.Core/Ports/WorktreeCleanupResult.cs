namespace WebDevLoop.Core.Ports;

public sealed record WorktreeCleanupResult(WorktreeCleanupOutcome Outcome, string? Warning = null);
