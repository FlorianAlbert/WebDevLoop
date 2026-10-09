namespace WebDevLoop.Core.Events;

/// <summary>Durable event outbox. Appended events persist with the next <c>IUnitOfWork.SaveChangesAsync</c>.</summary>
public interface IOutbox
{
    void Append(WorkflowEvent workflowEvent);

    Task<IReadOnlyList<EventEnvelope>> ReadPendingAsync(int maxCount, CancellationToken cancellationToken);

    /// <summary>Idempotent.</summary>
    Task MarkDispatchedAsync(long messageId, CancellationToken cancellationToken);

    Task RecordFailureAsync(long messageId, string error, CancellationToken cancellationToken);
}
