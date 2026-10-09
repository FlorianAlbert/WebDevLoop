using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfPullStackLayerRepository(WebDevLoopDbContext context) : IPullStackLayerRepository
{
    public async Task<IReadOnlyList<PullStackLayer>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await context.PullStackLayers
            .Where(layer => layer.SpecRunId == specRunId)
            .OrderBy(layer => layer.Position)
            .ToListAsync(cancellationToken);

    public void Add(PullStackLayer layer) => context.PullStackLayers.Add(layer);
}
