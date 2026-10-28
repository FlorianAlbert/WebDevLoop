using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Infrastructure.Queries;

public sealed class ParentBlockerQueries(IRunQueries runs, IRepositoryQueries repositories, IGitHubIssues issues) : IParentBlockerQueries
{
    private static readonly ParentBlockersView RepositoryMissing = new(
        [], "Blockers unavailable: the connected repository no longer exists. Check repository configuration and refresh.");

    public async Task<ParentBlockersView> GetBlockersAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        SpecRunView? run = await runs.GetSpecRunAsync(specRunId, cancellationToken);
        if (run is null || run.Status.IsTerminal())
        {
            return ParentBlockersView.NotEvaluated;
        }

        RepositoryView? repository = await repositories.GetAsync(run.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return RepositoryMissing;
        }

        IReadOnlyList<SpecRunView> relatedRuns = await runs.ListSpecRunsAsync(run.RepositoryId, cancellationToken);
        return await ReadAsync(run, repository, relatedRuns, new(StringComparer.OrdinalIgnoreCase), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, ParentBlockersView>> ListBlockersAsync(int repositoryId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SpecRunView> relatedRuns = await runs.ListSpecRunsAsync(repositoryId, cancellationToken);
        SpecRunView[] unfinished = relatedRuns.Where(run => !run.Status.IsTerminal()).ToArray();
        var result = new Dictionary<string, ParentBlockersView>();
        if (unfinished.Length == 0)
        {
            return result;
        }

        RepositoryView? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        var cache = new Dictionary<string, Task<IssueSnapshot>>(StringComparer.OrdinalIgnoreCase);
        foreach (SpecRunView run in unfinished)
        {
            result.Add(run.Id, repository is null
                ? RepositoryMissing
                : await ReadAsync(run, repository, relatedRuns, cache, cancellationToken));
        }

        return result;
    }

    private async Task<ParentBlockersView> ReadAsync(
        SpecRunView run,
        RepositoryView repository,
        IReadOnlyList<SpecRunView> relatedRuns,
        Dictionary<string, Task<IssueSnapshot>> cache,
        CancellationToken cancellationToken)
    {
        var parent = new IssueRef(repository.Owner, repository.Name, run.ParentIssueNumber);
        try
        {
            var reader = new ParentBlockerReader(ReadIssueAsync);
            IReadOnlyList<ParentBlocker> current = await reader.ReadAsync(parent, cancellationToken);
            var blockers = new List<ParentBlockerView>();
            foreach (ParentBlocker blocker in current)
            {
                IssueRef issue = blocker.Issue;
                SpecRunView? tracked = relatedRuns
                    .Where(candidate => SpecIssues.AreSame(new IssueRef(repository.Owner, repository.Name, candidate.ParentIssueNumber), issue))
                    .OrderByDescending(candidate => candidate.QueuePosition)
                    .FirstOrDefault();
                blockers.Add(new ParentBlockerView(
                    tracked?.Id, issue.ToString(), issue.Number, blocker.Title, tracked?.Status, blocker.IsSatisfied));
            }

            return new ParentBlockersView(blockers);
        }
        catch (GitHubIssuesException exception)
        {
            return new ParentBlockersView([], $"Blockers unavailable for {parent}: {exception.Message}");
        }

        Task<IssueSnapshot> ReadIssueAsync(IssueRef issue, CancellationToken token)
        {
            string key = $"{issue.Owner}/{issue.Repo}#{issue.Number}";
            if (!cache.TryGetValue(key, out Task<IssueSnapshot>? read))
            {
                read = issues.GetIssueAsync(issue, token);
                cache.Add(key, read);
            }

            return read;
        }
    }
}
