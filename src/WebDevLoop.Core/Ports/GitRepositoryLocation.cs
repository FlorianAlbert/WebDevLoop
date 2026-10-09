using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>The app-owned local clone of a registered repository.</summary>
public sealed record GitRepositoryLocation(GitHubRepoRef Repo, string CloneUrl, string LocalPath)
{
    public static GitRepositoryLocation From(RepositoryRecord repository) =>
        new(repository.Ref, repository.CloneUrl, repository.LocalPath);
}
