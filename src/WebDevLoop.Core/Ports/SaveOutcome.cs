namespace WebDevLoop.Core.Ports;

public enum SaveOutcome
{
    Saved,

    /// <summary>Another writer changed a tracked aggregate's <c>Version</c> first; nothing was saved.</summary>
    ConcurrencyConflict,
}
