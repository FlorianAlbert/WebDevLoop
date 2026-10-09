using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>
/// Adds parent spec issues to a repository's queue (entry point for the queue API). The native GitHub "blocked by"
/// relationships of the spec issue are loaded as <see cref="SpecDependency"/> rows; scheduling happens separately in
/// <see cref="SpecQueueScheduler"/> in reaction to <see cref="SpecRunQueued"/>.
/// </summary>
public sealed class SpecQueueService(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IGitHubIssues issues,
    IEffectiveSettingsProvider settings,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock)
{
    public async Task<EnqueueResult> EnqueueAsync(int repositoryId, int specIssueNumber, CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        if (repository is null)
        {
            return new EnqueueResult(EnqueueOutcome.RepositoryNotFound);
        }

        IReadOnlyList<SpecRun> queue = await specRuns.ListByRepositoryAsync(repositoryId, cancellationToken);
        if (queue.FirstOrDefault(run => !run.IsTerminal && run.ParentIssue.Number == specIssueNumber) is { } unfinished)
        {
            return new EnqueueResult(EnqueueOutcome.AlreadyQueued, unfinished.Id);
        }

        IssueSnapshot spec = await issues.GetIssueAsync(new IssueRef(repository.Owner, repository.Name, specIssueNumber), cancellationToken);
        EffectiveSettings effective = await settings.GetAsync(repositoryId, cancellationToken);
        DateTimeOffset now = clock.UtcNow;

        SpecRun run = SpecRun.Queue(ids.NewRunId(), repositoryId, spec.Ref, spec.Title, spec.Body, NextQueuePosition(queue), now);
        specRuns.Add(run);
        foreach (IssueRef blocking in spec.BlockedBy)
        {
            specRuns.AddDependency(DependencyOn(run, blocking, queue, effective.SpecDependencyMode));
        }

        outbox.Append(new SpecRunQueued(run.Id, repositoryId, now));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? new EnqueueResult(EnqueueOutcome.Queued, run.Id)
            : new EnqueueResult(EnqueueOutcome.ConcurrencyConflict);
    }

    private static int NextQueuePosition(IReadOnlyList<SpecRun> queue) =>
        queue.Count == 0 ? 1 : queue.Max(run => run.QueuePosition) + 1;

    /// <summary>A blocker that is a spec issue with an unfinished run in this queue links to that run; anything else stays an issue ref.</summary>
    private static SpecDependency DependencyOn(SpecRun blocked, IssueRef blocking, IReadOnlyList<SpecRun> queue, SpecDependencyMode mode) =>
        queue.FirstOrDefault(run => !run.IsTerminal && SpecIssues.AreSame(run.ParentIssue, blocking)) is { } blockingRun
            ? SpecDependency.OnSpecRun(blocked.Id, blockingRun.Id, mode)
            : SpecDependency.OnExternalIssue(blocked.Id, blocking, mode);
}
