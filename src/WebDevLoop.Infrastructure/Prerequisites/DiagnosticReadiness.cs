using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>
/// Holds the latest prerequisite evaluation so the web host can serve health/prerequisite pages and gate workflow
/// endpoints and hosted services on <see cref="ReadinessMode.Operational"/>. Nothing here throws on failed prerequisites.
/// </summary>
public sealed class DiagnosticReadiness(IPrerequisiteValidator validator, IClock clock)
{
    private volatile ReadinessSnapshot _current = ReadinessSnapshot.NotEvaluated;
    private readonly SemaphoreSlim _refresh = new(1, 1);

    public ReadinessSnapshot Current => _current;

    /// <summary>Raised synchronously after a new prerequisite evaluation is published.</summary>
    public event Action<ReadinessSnapshot>? Changed;

    public async Task<ReadinessSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            PrerequisiteReport report = await validator.ValidateAsync(cancellationToken);
            var snapshot = new ReadinessSnapshot(report, clock.UtcNow);
            _current = snapshot;
            Changed?.Invoke(snapshot);
            return snapshot;
        }
        finally
        {
            _refresh.Release();
        }
    }
}
