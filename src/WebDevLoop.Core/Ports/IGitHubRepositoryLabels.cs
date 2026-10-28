using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Provisions the fixed WebDevLoop Opt-in label, without changing any issue's labels.</summary>
public interface IGitHubRepositoryLabels
{
    Task<bool> HasOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken);

    Task CreateOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken);
}
