using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfOutboxMessageRepository(WebDevLoopDbContext context) : IOutboxMessageRepository
{
    public async Task<OutboxMessage?> GetAsync(long id, CancellationToken cancellationToken) =>
        await context.OutboxMessages.FirstOrDefaultAsync(message => message.Id == id, cancellationToken);

    public async Task<IReadOnlyList<OutboxMessage>> ListPendingAsync(int maxCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        return await context.OutboxMessages
            .Where(message => message.DispatchedAt == null && message.DeadLetteredAt == null)
            .OrderBy(message => message.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public void Add(OutboxMessage message) => context.OutboxMessages.Add(message);
}
