namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>Recovery stage: durably requests a frontier recomputation of every non-terminal spec run.</summary>
public interface IFrontierReconciliationTrigger
{
    /// <returns>The number of spec runs a recomputation was requested for.</returns>
    Task<int> RaiseAsync(CancellationToken cancellationToken);
}
