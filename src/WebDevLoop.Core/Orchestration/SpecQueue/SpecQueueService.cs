using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>
/// Adds currently open, opted-in parent spec issues with independently opted-in open sub-issues to a repository's queue.
/// The API and typed input share this submission-time check. The native GitHub "blocked by"
/// relationships of the spec issue are read fresh by the scheduler; scheduling happens separately in
/// <see cref="SpecQueueScheduler"/> in reaction to <see cref="SpecRunQueued"/>.
/// </summary>
public sealed class SpecQueueService(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IGitHubIssues issues,
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

        IssueSnapshot spec;
        try
        {
            spec = await issues.GetIssueAsync(new IssueRef(repository.Owner, repository.Name, specIssueNumber), cancellationToken);
            if (spec.State != IssueState.Open)
            {
                return new EnqueueResult(EnqueueOutcome.Ineligible, Reason: $"Parent spec #{specIssueNumber} must be open.");
            }

            if (!spec.Labels.Contains(WebDevLoopOptIn.Label, StringComparer.OrdinalIgnoreCase))
            {
                return new EnqueueResult(EnqueueOutcome.Ineligible, Reason: $"Parent spec #{specIssueNumber} requires the '{WebDevLoopOptIn.Label}' label (WebDevLoop Opt-in).");
            }

            IReadOnlyList<IssueSnapshot> tickets = await issues.GetSubIssuesAsync(spec.Ref, cancellationToken);
            if (!tickets.Any(ticket => ticket.State == IssueState.Open && ticket.Labels.Contains(WebDevLoopOptIn.Label, StringComparer.OrdinalIgnoreCase)))
            {
                return new EnqueueResult(EnqueueOutcome.Ineligible, Reason: $"Parent spec #{specIssueNumber} needs at least one open sub-issue independently labeled '{WebDevLoopOptIn.Label}'.");
            }
        }
        catch (GitHubIssuesException exception)
        {
            return new EnqueueResult(EnqueueOutcome.GitHubUnavailable, Reason: $"Could not verify current GitHub eligibility for #{specIssueNumber}. Check GitHub access and retry. {exception.Message}");
        }

        DateTimeOffset now = clock.UtcNow;

        SpecRun run = SpecRun.Queue(ids.NewRunId(), repositoryId, spec.Ref, spec.Title, spec.Body, NextQueuePosition(queue), now);
        specRuns.Add(run);
        outbox.Append(new SpecRunQueued(run.Id, repositoryId, now));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? new EnqueueResult(EnqueueOutcome.Queued, run.Id)
            : new EnqueueResult(EnqueueOutcome.ConcurrencyConflict);
    }

    private static int NextQueuePosition(IReadOnlyList<SpecRun> queue) =>
        queue.Count == 0 ? 1 : queue.Max(run => run.QueuePosition) + 1;

}
