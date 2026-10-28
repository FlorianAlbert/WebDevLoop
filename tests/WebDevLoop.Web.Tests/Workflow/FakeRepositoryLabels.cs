using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

internal sealed class FakeRepositoryLabels : IGitHubRepositoryLabels
{
    public bool Exists { get; set; } = true;

    public bool FailLookup { get; set; }

    public bool FailCreation { get; set; }

    public Task<bool> HasOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken) =>
        FailLookup ? throw new RepositoryLabelException("Check repository permissions and retry.") : Task.FromResult(Exists);

    public Task CreateOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        if (FailCreation)
        {
            throw new RepositoryLabelException("Check Issues write permissions and retry.");
        }

        Exists = true;
        return Task.CompletedTask;
    }
}
