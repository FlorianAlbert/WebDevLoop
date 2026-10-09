namespace WebDevLoop.Core.Queries;

public sealed record RepositoryView(
    int Id,
    string Owner,
    string Name,
    string DefaultBaseBranch,
    string CloneUrl,
    string LocalPath,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
