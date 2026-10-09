using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <param name="Released">Specs that are no longer active and gave up the active-spec slot they still held.</param>
/// <param name="Assigned">Active specs without a slot that got the lowest free one.</param>
/// <param name="ConcurrencyConflict">The repair lost a compare-and-swap race; the caller should retry with fresh state.</param>
public sealed record SlotReconciliation(IReadOnlyList<RunId> Released, IReadOnlyList<RunId> Assigned, bool ConcurrencyConflict)
{
    public static SlotReconciliation Nothing { get; } = new([], [], false);
}
