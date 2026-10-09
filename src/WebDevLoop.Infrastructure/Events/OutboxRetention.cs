using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Persistence;

namespace WebDevLoop.Infrastructure.Events;

/// <summary>
/// Keeps the outbox table small: dispatched messages older than the retention period are deleted. Pending and
/// dead-lettered messages are kept, since they still need delivery or diagnosis.
/// </summary>
public sealed class OutboxRetention(WebDevLoopDbContext context, IClock clock)
{
    /// <returns>How many messages were deleted.</returns>
    public Task<int> PurgeDispatchedAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = clock.UtcNow - retention;
        return context.OutboxMessages
            .Where(message => message.DispatchedAt != null && message.DispatchedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
