using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

/// <summary>Row-level outbox storage. Added rows are written by the same unit of work as the state change that raised them.</summary>
public interface IOutboxMessageRepository
{
    Task<OutboxMessage?> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>Undispatched messages, oldest first.</summary>
    Task<IReadOnlyList<OutboxMessage>> ListPendingAsync(int maxCount, CancellationToken cancellationToken);

    void Add(OutboxMessage message);
}
