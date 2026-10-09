namespace WebDevLoop.Core.Ports;

public enum WorktreeCleanupOutcome
{
    Removed,
    AlreadyMissing,
    RetainedDirty,
    RetainedLocked,
}
