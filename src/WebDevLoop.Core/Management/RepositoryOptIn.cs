using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Management;

/// <summary>Checks repository readiness and provisions the fixed label only after explicit approval.</summary>
public sealed class RepositoryOptIn(IGitHubRepositoryLabels labels)
{
    /// <summary>Checks presence without creating a label and supplies a ready-to-report diagnostic when lookup fails.</summary>
    public async Task<RepositoryLabelCheck> CheckAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        try
        {
            return await labels.HasOptInLabelAsync(repository, cancellationToken)
                ? new RepositoryLabelCheck.Available()
                : new RepositoryLabelCheck.Missing();
        }
        catch (RepositoryLabelException exception)
        {
            return new RepositoryLabelCheck.LookupFailed(new GitHubIssuesException(
                $"Could not check the '{WebDevLoopOptIn.Label}' label for {repository}. {exception.Message}", exception));
        }
    }

    public async Task<CommandResult<bool>> EnsureAsync(GitHubRepoRef repository, bool approveLabelCreation, CancellationToken cancellationToken)
    {
        switch (await CheckAsync(repository, cancellationToken))
        {
            case RepositoryLabelCheck.Available:
                return CommandResult<bool>.Succeeded(true);
            case RepositoryLabelCheck.LookupFailed(var error):
                return CommandResult<bool>.Unavailable(error.Message);
        }

        if (!approveLabelCreation)
        {
            return CommandResult<bool>.ApprovalRequired(
                $"The '{WebDevLoopOptIn.Label}' label is missing from {repository}. Explicit approval is required to create it. " +
                "Creating the label does not label existing issues. New work is paused; already selected work may finish.");
        }

        try
        {
            await labels.CreateOptInLabelAsync(repository, cancellationToken);
            return CommandResult<bool>.Succeeded(true);
        }
        catch (RepositoryLabelException exception)
        {
            return CommandResult<bool>.Unavailable($"Could not create the '{WebDevLoopOptIn.Label}' label for {repository}. {exception.Message}");
        }
    }
}
