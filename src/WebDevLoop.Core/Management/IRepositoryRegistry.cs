using WebDevLoop.Core.Queries;

namespace WebDevLoop.Core.Management;

public interface IRepositoryRegistry
{
    Task<CommandResult<RepositoryView>> RegisterAsync(RegisterRepositoryCommand command, CancellationToken cancellationToken);

    Task<CommandResult<RepositoryView>> UpdateAsync(int repositoryId, UpdateRepositoryCommand command, CancellationToken cancellationToken);

    /// <summary>A repository that ever had a spec run cannot be removed (disable it instead) so run history stays intact.</summary>
    Task<CommandResult<int>> RemoveAsync(int repositoryId, CancellationToken cancellationToken);
}
