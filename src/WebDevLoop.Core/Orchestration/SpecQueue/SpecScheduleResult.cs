using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <param name="Activated">Runs claimed into an active slot and moved to <see cref="SpecRunStatus.Preparing"/>, in queue order.</param>
/// <param name="Waiting">Runs that stay in <see cref="SpecRunStatus.WaitingForDependency"/> after this pass.</param>
/// <param name="ConcurrencyConflict">
/// A claim lost a compare-and-swap or active-slot race; the pass stopped and the caller should reschedule with a fresh unit of work.
/// </param>
public sealed record SpecScheduleResult(IReadOnlyList<RunId> Activated, IReadOnlyList<RunId> Waiting, bool ConcurrencyConflict)
{
    public static SpecScheduleResult Nothing { get; } = new([], [], false);
}
