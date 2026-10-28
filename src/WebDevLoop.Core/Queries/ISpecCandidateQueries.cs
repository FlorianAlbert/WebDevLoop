using WebDevLoop.Core.Management;

namespace WebDevLoop.Core.Queries;

/// <summary>Current GitHub candidates, searchable and filtered independently of ticket blockers.</summary>
public interface ISpecCandidateQueries
{
    Task<CommandResult<SpecCandidatePage>> ListAsync(
        int repositoryId, string? search, bool onlyUnblocked, int page, CancellationToken cancellationToken);
}
