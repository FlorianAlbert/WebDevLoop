using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Persistence;

namespace WebDevLoop.Infrastructure.Queries;

public sealed class EfRepositoryQueries(WebDevLoopDbContext context) : IRepositoryQueries
{
    public async Task<IReadOnlyList<RepositoryView>> ListAsync(CancellationToken cancellationToken)
    {
        List<RepositoryRecord> repositories = await context.Repositories.AsNoTracking().OrderBy(repository => repository.Id).ToListAsync(cancellationToken);
        return repositories.Select(repository => repository.ToView()).ToList();
    }

    public async Task<RepositoryView?> GetAsync(int repositoryId, CancellationToken cancellationToken) =>
        (await context.Repositories.AsNoTracking().FirstOrDefaultAsync(repository => repository.Id == repositoryId, cancellationToken))?.ToView();
}
