namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <param name="ParkedIntegrationRetryInterval">
/// How long a needs-attention ticket whose commit already moved the integration branch stays parked before reconciliation
/// resumes its saga again. Such a ticket blocks every later layer of its spec, so it is retried instead of waiting for the
/// user; the interval keeps a persistent failure from being retried on every pass.
/// </param>
public sealed record ExternalReconciliationOptions(TimeSpan ParkedIntegrationRetryInterval)
{
    public static readonly TimeSpan DefaultParkedIntegrationRetryInterval = TimeSpan.FromMinutes(15);

    public ExternalReconciliationOptions()
        : this(DefaultParkedIntegrationRetryInterval)
    {
    }
}
