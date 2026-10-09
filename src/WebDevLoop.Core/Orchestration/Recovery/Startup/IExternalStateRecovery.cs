using WebDevLoop.Core.Orchestration.Recovery.ExternalState;

namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>Recovery stage: re-derives run state from Git and GitHub, including merge tracking of awaiting-merge specs.</summary>
public interface IExternalStateRecovery
{
    Task<ExternalReconciliationReport> ReconcileAsync(CancellationToken cancellationToken);
}
