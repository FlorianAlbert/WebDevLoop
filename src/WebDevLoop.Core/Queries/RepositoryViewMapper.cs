using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

public static class RepositoryViewMapper
{
    public static RepositoryView ToView(this RepositoryRecord repository) => new(
        repository.Id,
        repository.Owner,
        repository.Name,
        repository.DefaultBaseBranch.Value,
        repository.CloneUrl,
        repository.LocalPath,
        repository.IsEnabled,
        repository.CreatedAt,
        repository.UpdatedAt);
}
