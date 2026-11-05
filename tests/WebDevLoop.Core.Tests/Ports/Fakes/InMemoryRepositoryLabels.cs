using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class InMemoryRepositoryLabels : IGitHubRepositoryLabels
{
    public bool Exists { get; set; } = true;

    public Dictionary<GitHubRepoRef, RepositoryLabelException> LookupFailures { get; } = [];

    /// <summary>Runs once, right after the next lookup returns, to model GitHub changing while a decision is in flight.</summary>
    public Action? AfterNextLookup { get; set; }

    public Task<bool> HasOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        if (LookupFailures.TryGetValue(repository, out RepositoryLabelException? failure))
        {
            throw failure;
        }

        bool exists = Exists;
        Action? change = AfterNextLookup;
        AfterNextLookup = null;
        change?.Invoke();
        return Task.FromResult(exists);
    }

    public Task CreateOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        Exists = true;
        return Task.CompletedTask;
    }
}
