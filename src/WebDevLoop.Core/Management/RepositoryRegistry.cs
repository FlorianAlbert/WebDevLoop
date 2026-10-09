using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Management;

public sealed class RepositoryRegistry(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IUnitOfWork unitOfWork,
    ICurrentRepositorySelection selection,
    IClock clock) : IRepositoryRegistry
{
    public async Task<CommandResult<RepositoryView>> RegisterAsync(RegisterRepositoryCommand command, CancellationToken cancellationToken)
    {
        var errors = new List<SettingsValidationError>();
        GitHubRepoRef? repo = RequireRepoRef(command, errors);
        if (string.IsNullOrWhiteSpace(command.LocalPath))
        {
            errors.Add(Required(nameof(command.LocalPath)));
        }

        BranchName? baseBranch = ParseBranch(command.DefaultBaseBranch ?? DefaultSettings.BaseBranch.Value, nameof(command.DefaultBaseBranch), errors);
        if (errors.Count > 0)
        {
            return CommandResult<RepositoryView>.Invalid(errors);
        }

        if (await repositories.FindAsync(repo!.Value, cancellationToken) is not null)
        {
            return CommandResult<RepositoryView>.Conflict($"Repository '{repo}' is already registered.");
        }

        DateTimeOffset now = clock.UtcNow;
        string cloneUrl = string.IsNullOrWhiteSpace(command.CloneUrl) ? GitHubCloneUrl(repo.Value) : command.CloneUrl;
        var repository = RepositoryRecord.Register(repo.Value, baseBranch!.Value, cloneUrl, command.LocalPath!, now);
        repository.SetEnabled(true, now);
        repositories.Add(repository);

        return await SaveAsync(repository, cancellationToken);
    }

    public async Task<CommandResult<RepositoryView>> UpdateAsync(int repositoryId, UpdateRepositoryCommand command, CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        if (repository is null)
        {
            return CommandResult<RepositoryView>.NotFound($"Repository {repositoryId} does not exist.");
        }

        var errors = new List<SettingsValidationError>();
        BranchName? baseBranch = command.DefaultBaseBranch is null ? null : ParseBranch(command.DefaultBaseBranch, nameof(command.DefaultBaseBranch), errors);
        if (command.CloneUrl is not null && string.IsNullOrWhiteSpace(command.CloneUrl))
        {
            errors.Add(Required(nameof(command.CloneUrl)));
        }

        if (command.LocalPath is not null && string.IsNullOrWhiteSpace(command.LocalPath))
        {
            errors.Add(Required(nameof(command.LocalPath)));
        }

        if (errors.Count > 0)
        {
            return CommandResult<RepositoryView>.Invalid(errors);
        }

        repository.DefaultBaseBranch = baseBranch ?? repository.DefaultBaseBranch;
        repository.CloneUrl = command.CloneUrl ?? repository.CloneUrl;
        repository.LocalPath = command.LocalPath ?? repository.LocalPath;
        repository.SetEnabled(command.IsEnabled ?? repository.IsEnabled, clock.UtcNow);

        return await SaveAsync(repository, cancellationToken);
    }

    public async Task<CommandResult<int>> RemoveAsync(int repositoryId, CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        if (repository is null)
        {
            return CommandResult<int>.NotFound($"Repository {repositoryId} does not exist.");
        }

        if ((await specRuns.ListByRepositoryAsync(repositoryId, cancellationToken)).Count > 0)
        {
            return CommandResult<int>.Conflict($"Repository '{repository.Ref}' has spec runs and cannot be removed; disable it instead.");
        }

        repositories.Remove(repository);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return CommandResult<int>.Conflict("The repository was changed concurrently; retry.");
        }

        if (selection.CurrentRepositoryId == repositoryId)
        {
            selection.Select(null);
        }

        return CommandResult<int>.Succeeded(repositoryId);
    }

    private async Task<CommandResult<RepositoryView>> SaveAsync(RepositoryRecord repository, CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? CommandResult<RepositoryView>.Succeeded(repository.ToView())
            : CommandResult<RepositoryView>.Conflict("The repository was changed concurrently; retry.");

    private static GitHubRepoRef? RequireRepoRef(RegisterRepositoryCommand command, List<SettingsValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(command.Owner))
        {
            errors.Add(Required(nameof(command.Owner)));
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            errors.Add(Required(nameof(command.Name)));
        }

        return errors.Count == 0 ? new GitHubRepoRef(command.Owner!, command.Name!) : null;
    }

    private static BranchName? ParseBranch(string value, string field, List<SettingsValidationError> errors)
    {
        try
        {
            return new BranchName(value);
        }
        catch (ArgumentException)
        {
            errors.Add(new SettingsValidationError(field, $"'{value}' is not a valid branch name."));
            return null;
        }
    }

    private static SettingsValidationError Required(string field) => new(field, "A value is required.");

    private static string GitHubCloneUrl(GitHubRepoRef repo) => $"https://github.com/{repo.Owner}/{repo.Name}.git";
}
