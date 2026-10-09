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

    /// <summary>
    /// Repairs a repository's active-slot bookkeeping (recovery and periodic reconciliation; run before
    /// <see cref="ScheduleRepositoryAsync"/>). A spec that is no longer active but still holds a slot (parked in
    /// <see cref="SpecRunStatus.NeedsAttention"/>, aborted, or finished before slots were released on leaving the active
    /// phases) gives it up, so the number cannot collide with another spec's claim once it becomes active again; an active
    /// spec without a slot gets the lowest free one, so the unique (repository, slot) index guards it again. All changes
    /// are saved with one compare-and-swap.
    /// </summary>
    public async Task<SlotReconciliation> ReconcileSlotsAsync(int repositoryId, CancellationToken cancellationToken)
    {
        EffectiveSettings effective = await settings.GetAsync(repositoryId, cancellationToken);
        IReadOnlyList<SpecRun> queue = await specRuns.ListByRepositoryAsync(repositoryId, cancellationToken);
        SpecRun[] stale = queue.Where(run => !run.IsActive && run.MaxActiveSpecsSlot is not null).ToArray();
        foreach (SpecRun run in stale)
        {
            run.MaxActiveSpecsSlot = null;
        }

        var unoccupied = new Queue<int>(UnoccupiedSlots(queue, effective.MaxActiveSpecsPerRepo));
        var assigned = new List<RunId>();
        foreach (SpecRun run in queue.Where(run => run.IsActive && run.MaxActiveSpecsSlot is null).OrderBy(run => run.QueuePosition))
        {
            if (!unoccupied.TryDequeue(out int slot))
            {
                break;
            }

            run.MaxActiveSpecsSlot = slot;
            assigned.Add(run.Id);
        }

        if (stale.Length == 0 && assigned.Count == 0)
        {
            return SlotReconciliation.Nothing;
        }

        bool saved = await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
        return saved ? new SlotReconciliation(stale.Select(run => run.Id).ToArray(), assigned, false) : new SlotReconciliation([], [], true);
    }

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

    /// <summary>
    /// The lowest free active-spec slot of the repository, or null while <c>MaxActiveSpecsPerRepo</c> specs are active. Used
    /// when a spec becomes active again outside the queue (a retried spec); the unique (repository, slot) index still decides
    /// a race with a concurrent claim.
    /// </summary>
    public async Task<int?> FindFreeSlotAsync(int repositoryId, CancellationToken cancellationToken)
    {
        EffectiveSettings effective = await settings.GetAsync(repositoryId, cancellationToken);
        IReadOnlyList<SpecRun> queue = await specRuns.ListByRepositoryAsync(repositoryId, cancellationToken);
        return FreeSlots(queue, effective.MaxActiveSpecsPerRepo).Select(slot => (int?)slot).FirstOrDefault();
    }

    private static bool IsWaitingToStart(SpecRun run) =>
        run.Status is SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency;

    /// <summary>Lowest free slot numbers first, so concurrent schedulers collide on the same slot instead of overfilling.</summary>
    private static IEnumerable<int> FreeSlots(IReadOnlyList<SpecRun> queue, int maxActiveSpecs) =>
        UnoccupiedSlots(queue, maxActiveSpecs).Take(Math.Max(0, maxActiveSpecs - queue.Count(run => run.IsActive)));

    /// <summary>Slot numbers 1..<paramref name="maxActiveSpecs"/> no active spec holds, lowest first.</summary>
    private static IEnumerable<int> UnoccupiedSlots(IReadOnlyList<SpecRun> queue, int maxActiveSpecs)
    {
        HashSet<int> occupied = queue.Where(run => run.IsActive && run.MaxActiveSpecsSlot is not null).Select(run => run.MaxActiveSpecsSlot!.Value).ToHashSet();
        return Enumerable.Range(1, maxActiveSpecs).Where(slot => !occupied.Contains(slot));
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
