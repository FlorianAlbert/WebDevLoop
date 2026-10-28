using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Queries;

/// <summary>Fresh, paginated GitHub discovery. Failures propagate rather than authorizing stale candidates.</summary>
public sealed class SpecCandidateQueries(IRepositoryQueries repositories, IRunQueries runs, IGitHubIssues issues) : ISpecCandidateQueries
{
    private readonly ParentBlockerReader _blockers = new(issues.GetIssueAsync);

    public async Task<CommandResult<SpecCandidatePage>> ListAsync(
        int repositoryId, string? search, bool onlyUnblocked, int page, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        RepositoryView? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        if (repository is null)
        {
            return CommandResult<SpecCandidatePage>.NotFound($"Repository {repositoryId} does not exist.");
        }

        var unavailable = (await runs.ListSpecRunsAsync(repositoryId, cancellationToken))
            .Where(run => !run.Status.IsTerminal()).Select(run => run.ParentIssueNumber).ToHashSet();
        string term = (search ?? string.Empty).Trim().TrimStart('#');
        IReadOnlyList<SpecCandidateView> candidates;
        int? next = page;
        do
        {
            ParentIssuePage source = await issues.ListOpenParentIssuesAsync(new(repository.Owner, repository.Name), next.Value, cancellationToken);
            next = source.NextPage;
            candidates = await FilterPageAsync(source.Issues, unavailable, term, onlyUnblocked, cancellationToken);
        }
        while (candidates.Count == 0 && next.HasValue);

        return CommandResult<SpecCandidatePage>.Succeeded(new(candidates, next));
    }

    private async Task<IReadOnlyList<SpecCandidateView>> FilterPageAsync(
        IReadOnlyList<ParentIssueSummary> parents, IReadOnlySet<int> unavailable, string term,
        bool onlyUnblocked, CancellationToken cancellationToken)
    {
        var candidates = new List<SpecCandidateView>();
        foreach (ParentIssueSummary summary in parents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (unavailable.Contains(summary.Ref.Number) || !MatchesSearch(summary, term))
            {
                continue;
            }

            (IssueSnapshot spec, string? rejection) = await SpecEligibility.ReadAsync(issues, summary.Ref, cancellationToken);
            if (rejection is not null || (onlyUnblocked && !await _blockers.AreAllSatisfiedAsync(spec, cancellationToken)))
            {
                continue;
            }

            candidates.Add(new(spec.Ref.Number, spec.Title));
        }

        return candidates;
    }

    private static bool MatchesSearch(ParentIssueSummary summary, string term) =>
        summary.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
        || summary.Ref.Number.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal);
}
