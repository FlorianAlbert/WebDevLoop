using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <summary>Current native parent blockers, isolated from local run reads. Finished runs are not queried; lookup failures are explicit.</summary>
public interface IParentBlockerQueries
{
    Task<ParentBlockersView> GetBlockersAsync(RunId specRunId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, ParentBlockersView>> ListBlockersAsync(int repositoryId, CancellationToken cancellationToken);
}

/// <summary>The display result for a spec's parent blockers, including availability and whether the list was evaluated.</summary>
public sealed record ParentBlockersView(
    IReadOnlyList<ParentBlockerView> Blockers,
    string? UnavailableReason = null,
    bool Evaluated = true)
{
    public static ParentBlockersView NotEvaluated { get; } = new([], Evaluated: false);
}
