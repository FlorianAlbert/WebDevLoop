namespace WebDevLoop.Core.Domain;

public sealed class RepositoryRecord : VersionedEntity
{
    private RepositoryRecord()
    {
        Owner = string.Empty;
        Name = string.Empty;
        CloneUrl = string.Empty;
        LocalPath = string.Empty;
    }

    public int Id { get; private set; }

    public string Owner { get; private set; }

    public string Name { get; private set; }

    public BranchName DefaultBaseBranch { get; set; }

    public string CloneUrl { get; set; }

    public string LocalPath { get; set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public GitHubRepoRef Ref => new(Owner, Name);

    public static RepositoryRecord Register(GitHubRepoRef repo, BranchName defaultBaseBranch, string cloneUrl, string localPath, DateTimeOffset at) => new()
    {
        Owner = repo.Owner,
        Name = repo.Name,
        DefaultBaseBranch = defaultBaseBranch,
        CloneUrl = cloneUrl,
        LocalPath = localPath,
        CreatedAt = at,
        UpdatedAt = at,
    };

    public void SetEnabled(bool isEnabled, DateTimeOffset at)
    {
        IsEnabled = isEnabled;
        UpdatedAt = at;
    }
}
