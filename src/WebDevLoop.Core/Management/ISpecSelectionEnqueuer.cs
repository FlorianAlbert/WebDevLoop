namespace WebDevLoop.Core.Management;

/// <summary>Enqueues each selection in order and returns a result for every spec.</summary>
public interface ISpecSelectionEnqueuer
{
    Task<IReadOnlyList<SpecSelectionResult>> EnqueueAsync(
        int repositoryId, IReadOnlyList<int> issueNumbers, CancellationToken cancellationToken);
}
