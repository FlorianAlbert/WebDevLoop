using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfRepositoryRecordRepository(WebDevLoopDbContext context) : IRepositoryRecordRepository
{
    public async Task<RepositoryRecord?> GetAsync(int id, CancellationToken cancellationToken) =>
        await context.Repositories.FirstOrDefaultAsync(repository => repository.Id == id, cancellationToken);

    public async Task<RepositoryRecord?> FindAsync(GitHubRepoRef repo, CancellationToken cancellationToken) =>
        await context.Repositories.FirstOrDefaultAsync(
            repository => repository.Owner == repo.Owner && repository.Name == repo.Name,
            cancellationToken);

    public async Task<IReadOnlyList<RepositoryRecord>> ListAsync(CancellationToken cancellationToken) =>
        await context.Repositories.OrderBy(repository => repository.Id).ToListAsync(cancellationToken);

    public void Add(RepositoryRecord repository) => context.Repositories.Add(repository);

    public void Remove(RepositoryRecord repository) => context.Repositories.Remove(repository);
}
