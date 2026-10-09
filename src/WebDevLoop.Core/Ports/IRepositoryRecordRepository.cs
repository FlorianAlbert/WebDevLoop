using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface IRepositoryRecordRepository
{
    Task<RepositoryRecord?> GetAsync(int id, CancellationToken cancellationToken);

    Task<RepositoryRecord?> FindAsync(GitHubRepoRef repo, CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositoryRecord>> ListAsync(CancellationToken cancellationToken);

    void Add(RepositoryRecord repository);

    void Remove(RepositoryRecord repository);
}
