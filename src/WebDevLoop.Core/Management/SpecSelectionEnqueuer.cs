using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Management;

/// <summary>Sequential batch enqueue over independent attempts supplied by the host.</summary>
public sealed class SpecSelectionEnqueuer(ISpecEnqueuer enqueuer) : ISpecSelectionEnqueuer
{
    public async Task<IReadOnlyList<SpecSelectionResult>> EnqueueAsync(
        int repositoryId, IReadOnlyList<int> issueNumbers, CancellationToken cancellationToken)
    {
        var results = new List<SpecSelectionResult>();
        foreach (int number in issueNumbers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnqueueResult result = await enqueuer.EnqueueAsync(repositoryId, number, cancellationToken);
            results.Add(new(number, result));
        }

        return results;
    }
}
