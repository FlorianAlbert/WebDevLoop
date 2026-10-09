using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed record ReadinessSnapshot(PrerequisiteReport? Report, DateTimeOffset? EvaluatedAt)
{
    public static ReadinessSnapshot NotEvaluated { get; } = new(null, null);

    public ReadinessMode Mode => Report is { IsReady: true } ? ReadinessMode.Operational : ReadinessMode.DiagnosticOnly;

    public IReadOnlyList<PrerequisiteCheck> FailedChecks =>
        Report?.Checks.Where(check => check.Status == PrerequisiteStatus.Failed).ToArray() ?? [];
}
