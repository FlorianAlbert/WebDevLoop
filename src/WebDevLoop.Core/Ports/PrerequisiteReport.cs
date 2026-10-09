namespace WebDevLoop.Core.Ports;

public sealed record PrerequisiteReport(IReadOnlyList<PrerequisiteCheck> Checks)
{
    /// <summary>Warnings do not block; any failure keeps the app in diagnostic-only mode.</summary>
    public bool IsReady => Checks.All(check => check.Status != PrerequisiteStatus.Failed);
}
