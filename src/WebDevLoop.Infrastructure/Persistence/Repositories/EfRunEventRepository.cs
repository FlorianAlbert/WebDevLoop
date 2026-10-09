using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfRunEventRepository(WebDevLoopDbContext context) : IRunEventRepository
{
    public async Task<IReadOnlyList<RunEvent>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await context.RunEvents
            .Where(runEvent => runEvent.SpecRunId == specRunId)
            .OrderBy(runEvent => runEvent.Id)
            .ToListAsync(cancellationToken);

    public void Add(RunEvent runEvent) => context.RunEvents.Add(runEvent);
}
