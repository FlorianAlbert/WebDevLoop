using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Completion;
using WebDevLoop.Core.Orchestration.WorkSelection;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>
/// One scheduling pass over a repository queue (entry point for the queue worker; call on <see cref="SpecRunQueued"/>,
/// on <see cref="SpecRunStatusChanged"/> of the repository, and periodically). Queued specs are visited in queue order:
/// current eligibility is rechecked before starting work, and ineligible specs wait with a reason without claiming a slot.
/// Dependency-blocked specs move to <see cref="SpecRunStatus.WaitingForDependency"/> without holding up independent specs
/// behind them, and startable specs claim a free active slot (1..<c>MaxActiveSpecsPerRepo</c>) and move to
/// <see cref="SpecRunStatus.Preparing"/>. Each claim is saved separately with compare-and-swap; the persisted unique
/// (repository, slot) index makes a concurrent claim of the same slot lose with <see cref="SaveOutcome.ConcurrencyConflict"/>.
/// Parked <see cref="SpecRunStatus.WaitingForTickets"/> runs recheck current eligibility and blockers and claim a free slot
/// to resume directly at <see cref="SpecRunStatus.Running"/>, without repeating preparation or preempting active work.
/// Parent opt-out and no-work outcomes complete parked runs without claiming a slot.
/// </summary>
public sealed class SpecQueueScheduler(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IGitHubIssues issues,
    IEffectiveSettingsProvider settings,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    RepositoryOptIn optIn,
    ITicketRunRepository ticketRuns)
{
    private readonly SpecDependencyGate _gate = new(issues);
    private readonly CurrentWorkEligibility _eligibility = new(issues);
    private readonly SpecRunJournal _journal = new(outbox, clock);

    public async Task<SpecScheduleResult> ScheduleRepositoryAsync(int repositoryId, CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        if (repository is not { IsEnabled: true })
        {
            return SpecScheduleResult.Nothing;
        }

        RepositoryLabelCheck label = await optIn.CheckAsync(repository.Ref, cancellationToken);
        if (label.ToDeferredResult(
            SpecScheduleResult.Nothing,
            error => SpecScheduleResult.Nothing with { LookupError = error }) is { } deferred)
        {
            return deferred;
        }

        EffectiveSettings effective = await settings.GetAsync(repositoryId, cancellationToken);
        IReadOnlyList<SpecRun> queue = await specRuns.ListByRepositoryAsync(repositoryId, cancellationToken);
        var freeSlots = new Queue<int>(FreeSlots(queue, effective.MaxActiveSpecsPerRepo));
        var activated = new List<RunId>();
        var waiting = new List<RunId>();
        var faults = new List<SpecScheduleFault>();

        foreach (SpecRun run in queue.Where(IsWaitingToStart))
        {
            SpecScheduleResult result = run.Status == SpecRunStatus.WaitingForTickets
                ? await ScheduleResumeAsync(run, freeSlots, cancellationToken)
                : await ScheduleStartAsync(run, freeSlots, effective.BaseBranch, cancellationToken);
            activated.AddRange(result.Activated);
            waiting.AddRange(result.Waiting);
            faults.AddRange(result.Faults);
            if (result.ConcurrencyConflict)
            {
                return new SpecScheduleResult(activated, waiting, ConcurrencyConflict: true) { Faults = faults };
            }
        }

        return new SpecScheduleResult(activated, waiting, ConcurrencyConflict: false) { Faults = faults };
    }

    private async Task<SpecScheduleResult> ScheduleStartAsync(
        SpecRun run, Queue<int> freeSlots, BranchName baseBranch, CancellationToken cancellationToken)
    {
        SpecStartDecision decision;
        try
        {
            (_, string? rejection) = await SpecEligibility.ReadAsync(issues, run.ParentIssue, cancellationToken);
            if (rejection is not null)
            {
                bool saved = await TrySaveReasonAsync(run, $"Start deferred. {rejection} Restore eligibility to let scheduling retry.", cancellationToken);
                return new SpecScheduleResult([], [run.Id], ConcurrencyConflict: !saved);
            }

            decision = await _gate.EvaluateAsync(run, cancellationToken);
        }
        catch (GitHubIssuesException exception)
        {
            string reason = $"Cannot verify current GitHub eligibility or blockers for {run.ParentIssue}. "
                + $"Check issue access and GitHub availability, then refresh or retry queue recovery. {exception.Message}";
            bool saved = await TrySaveReasonAsync(run, reason, cancellationToken);
            return new SpecScheduleResult([], [run.Id], ConcurrencyConflict: !saved)
            {
                Faults = [new SpecScheduleFault(run.Id, reason)],
            };
        }

        if (!await TrySaveReasonAsync(run, null, cancellationToken))
        {
            return new SpecScheduleResult([], [], ConcurrencyConflict: true);
        }

        if (decision.Kind == SpecStartKind.Wait)
        {
            bool saved = run.Status != SpecRunStatus.Queued
                || await TrySaveTransitionAsync(run, SpecRunStatus.WaitingForDependency, cancellationToken);
            return new SpecScheduleResult([], [run.Id], ConcurrencyConflict: !saved);
        }

        if (!freeSlots.TryDequeue(out int slot))
        {
            return SpecScheduleResult.Nothing;
        }

        run.BaseBranch = baseBranch;
        return await ClaimSlotAsync(run, slot, SpecRunStatus.Preparing, cancellationToken);
    }

    private async Task<SpecScheduleResult> ScheduleResumeAsync(
        SpecRun run, Queue<int> freeSlots, CancellationToken cancellationToken)
    {
        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(run.Id, cancellationToken);
        CurrentWork current = await _eligibility.ReadAsync(run, tickets, cancellationToken);
        if (current.GetTerminalReason(tickets) is { } terminalReason)
        {
            _journal.Move(run, SpecRunStatus.Completed);
            run.SetWorkDecisionReason(terminalReason);
            bool saved = await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
            return new SpecScheduleResult([], [], ConcurrencyConflict: !saved);
        }

        string? reason = current.Errors.FirstOrDefault()?.Message
            ?? (current.HasWork(tickets) ? current.WaitReason : null)
            ?? (current.AllEligibleBlocked ? CurrentWork.BlockedReason : null);
        if (reason is not null)
        {
            bool saved = await TrySaveReasonAsync(run, reason, cancellationToken);
            return new SpecScheduleResult([], [run.Id], ConcurrencyConflict: !saved)
            {
                Faults = current.Errors.Select(error => new SpecScheduleFault(run.Id, error.Message)).ToArray(),
            };
        }

        if (!freeSlots.TryDequeue(out int slot))
        {
            bool saved = await TrySaveReasonAsync(run, CurrentWork.ResumeSlotWaitReason, cancellationToken);
            return new SpecScheduleResult([], [], ConcurrencyConflict: !saved);
        }

        return await ClaimSlotAsync(run, slot, SpecRunStatus.Running, cancellationToken);
    }

    private async Task<SpecScheduleResult> ClaimSlotAsync(
        SpecRun run, int slot, SpecRunStatus next, CancellationToken cancellationToken)
    {
        run.MaxActiveSpecsSlot = slot;
        bool saved = await TrySaveTransitionAsync(run, next, cancellationToken);
        return new SpecScheduleResult(saved ? [run.Id] : [], [], ConcurrencyConflict: !saved);
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
        run.Status is SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency or SpecRunStatus.WaitingForTickets;

    private async Task<bool> TrySaveReasonAsync(SpecRun run, string? reason, CancellationToken cancellationToken)
    {
        if (run.WorkDecisionReason == reason)
        {
            return true;
        }

        run.SetWorkDecisionReason(reason);
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
    }

    /// <summary>Lowest free slot numbers first, so concurrent schedulers collide on the same slot instead of overfilling.</summary>
    private static IEnumerable<int> FreeSlots(IReadOnlyList<SpecRun> queue, int maxActiveSpecs)
    {
        SpecRun[] active = queue.Where(run => run.IsActive).ToArray();
        HashSet<int> occupied = active.Where(run => run.MaxActiveSpecsSlot is not null).Select(run => run.MaxActiveSpecsSlot!.Value).ToHashSet();
        return Enumerable.Range(1, maxActiveSpecs).Where(slot => !occupied.Contains(slot)).Take(Math.Max(0, maxActiveSpecs - active.Length));
    }

    private async Task<bool> TrySaveTransitionAsync(SpecRun run, SpecRunStatus next, CancellationToken cancellationToken)
    {
        _journal.Move(run, next);
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
    }
}
