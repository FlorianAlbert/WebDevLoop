namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>
/// Startup recovery stage: delivers the outbox messages a previous process persisted but never dispatched. Subscribers
/// deduplicate by message id, so a message delivered before the crash but not marked is harmless.
/// </summary>
public interface IOutboxReplay
{
    /// <returns>The number of messages delivered.</returns>
    Task<int> ReplayPendingAsync(CancellationToken cancellationToken);
}
