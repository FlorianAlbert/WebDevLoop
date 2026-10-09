using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>
/// One scheduling pass over a repository queue (entry point for the queue worker; call on <see cref="SpecRunQueued"/>,
/// on <see cref="SpecRunStatusChanged"/> of the repository, and periodically). Queued specs are visited in queue order:
/// dependency-blocked specs move to <see cref="SpecRunStatus.WaitingForDependency"/> without holding up independent specs
/// behind them, and startable specs claim a free active slot (1..<c>MaxActiveSpecsPerRepo</c>) and move to
/// <see cref="SpecRunStatus.Preparing"/>. Each claim is saved separately with compare-and-swap; the persisted unique
/// (repository, slot) index makes a concurrent claim of the same slot lose with <see cref="SaveOutcome.ConcurrencyConflict"/>.
/// </summary>
public sealed class SpecQueueScheduler(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IGitHubIssues issues,
    IEffectiveSettingsProvider settings,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private readonly SpecDependencyGate _gate = new(specRuns, issues);

    public async Task<SpecScheduleResult> ScheduleRepositoryAsync(int repositoryId, CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        if (repository is not { IsEnabled: true })
        {
            return SpecScheduleResult.Nothing;
        }

        EffectiveSettings effective = await settings.GetAsync(repositoryId, cancellationToken);
        IReadOnlyList<SpecRun> queue = await specRuns.ListByRepositoryAsync(repositoryId, cancellationToken);
        var freeSlots = new Queue<int>(FreeSlots(queue, effective.MaxActiveSpecsPerRepo));
        var activated = new List<RunId>();
        var waiting = new List<RunId>();

        foreach (SpecRun run in queue.Where(IsWaitingToStart))
        {
            SpecStartDecision decision = await _gate.EvaluateAsync(run, queue, effective.SpecDependencyMode, cancellationToken);
            if (decision.Kind == SpecStartKind.Wait)
            {
                waiting.Add(run.Id);
                if (run.Status == SpecRunStatus.Queued
                    && !await TrySaveTransitionAsync(run, SpecRunStatus.WaitingForDependency, cancellationToken))
                {
                    return new SpecScheduleResult(activated, waiting, ConcurrencyConflict: true);
                }

                continue;
            }

            if (!freeSlots.TryDequeue(out int slot))
            {
                continue;
            }

            PrepareClaim(run, slot, decision, effective.BaseBranch);
            if (!await TrySaveTransitionAsync(run, SpecRunStatus.Preparing, cancellationToken))
            {
                return new SpecScheduleResult(activated, waiting, ConcurrencyConflict: true);
            }

            activated.Add(run.Id);
        }

        return new SpecScheduleResult(activated, waiting, ConcurrencyConflict: false);
    }

    private static bool IsWaitingToStart(SpecRun run) =>
        run.Status is SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency;

    /// <summary>Lowest free slot numbers first, so concurrent schedulers collide on the same slot instead of overfilling.</summary>
    private static IEnumerable<int> FreeSlots(IReadOnlyList<SpecRun> queue, int maxActiveSpecs)
    {
        SpecRun[] active = queue.Where(run => run.IsActive).ToArray();
        HashSet<int> occupied = active.Where(run => run.MaxActiveSpecsSlot is not null).Select(run => run.MaxActiveSpecsSlot!.Value).ToHashSet();
        return Enumerable.Range(1, maxActiveSpecs).Where(slot => !occupied.Contains(slot)).Take(Math.Max(0, maxActiveSpecs - active.Length));
    }

    private static void PrepareClaim(SpecRun run, int slot, SpecStartDecision decision, BranchName trunk)
    {
        run.MaxActiveSpecsSlot = slot;
        run.BaseBranch = trunk;
        if (decision.Kind == SpecStartKind.StackOnTop)
        {
            run.DependencyModeUsed = SpecDependencyMode.StackOnTop;
            run.IntegrationBaseSha = decision.StackBaseSha;
        }
        else if (run.Status == SpecRunStatus.WaitingForDependency)
        {
            run.DependencyModeUsed = SpecDependencyMode.WaitForMerge;
        }
    }

    private async Task<bool> TrySaveTransitionAsync(SpecRun run, SpecRunStatus next, CancellationToken cancellationToken)
    {
        SpecRunStatus previous = run.Status;
        DateTimeOffset now = clock.UtcNow;
        run.TransitionTo(next, now);
        outbox.Append(new SpecRunStatusChanged(run.Id, run.RepositoryId, previous, next, now));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
    }
}
