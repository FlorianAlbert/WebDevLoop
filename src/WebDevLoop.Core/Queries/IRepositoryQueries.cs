namespace WebDevLoop.Core.Queries;

public interface IRepositoryQueries
{
    Task<IReadOnlyList<RepositoryView>> ListAsync(CancellationToken cancellationToken);

    Task<RepositoryView?> GetAsync(int repositoryId, CancellationToken cancellationToken);
}
