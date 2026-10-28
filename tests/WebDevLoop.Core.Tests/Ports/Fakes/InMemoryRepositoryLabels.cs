using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class InMemoryRepositoryLabels : IGitHubRepositoryLabels
{
    public bool Exists { get; set; } = true;

    public Dictionary<GitHubRepoRef, RepositoryLabelException> LookupFailures { get; } = [];

    public Task<bool> HasOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken) =>
        LookupFailures.TryGetValue(repository, out RepositoryLabelException? failure) ? throw failure : Task.FromResult(Exists);

    public Task CreateOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        Exists = true;
        return Task.CompletedTask;
    }
}
