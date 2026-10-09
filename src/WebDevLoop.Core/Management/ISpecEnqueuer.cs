using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Management;

/// <summary>Application entry point for adding a parent spec issue to a repository's queue.</summary>
public interface ISpecEnqueuer
{
    Task<EnqueueResult> EnqueueAsync(int repositoryId, int specIssueNumber, CancellationToken cancellationToken);
}
