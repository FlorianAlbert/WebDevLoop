using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>
/// Holds the latest prerequisite evaluation so the web host can serve health/prerequisite pages and gate workflow
/// endpoints and hosted services on <see cref="ReadinessMode.Operational"/>. Nothing here throws on failed prerequisites.
/// </summary>
public sealed class DiagnosticReadiness(IPrerequisiteValidator validator, IClock clock)
{
    private volatile ReadinessSnapshot _current = ReadinessSnapshot.NotEvaluated;

    public ReadinessSnapshot Current => _current;

    public async Task<ReadinessSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        PrerequisiteReport report = await validator.ValidateAsync(cancellationToken);
        var snapshot = new ReadinessSnapshot(report, clock.UtcNow);
        _current = snapshot;
        return snapshot;
    }
}
