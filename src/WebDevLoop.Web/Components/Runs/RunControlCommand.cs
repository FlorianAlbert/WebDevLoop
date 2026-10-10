using WebDevLoop.Core.Orchestration.Control;

namespace WebDevLoop.Web.Components.Runs;

/// <param name="Key">Suffix of the button's <c>data-testid</c> (<c>control-{Key}</c>).</param>
/// <param name="IsAvailable">The run's state allows the command.</param>
/// <param name="UnavailableReason">Why the command is disabled while <paramref name="IsAvailable"/> is false.</param>
/// <param name="Confirmation">Question asked before a destructive command runs; null runs it right away.</param>
public sealed record RunControlCommand(
    string Key,
    string Label,
    bool IsAvailable,
    string? UnavailableReason,
    string? Confirmation,
    Func<IRunControl, CancellationToken, Task<ControlResult>> Execute)
{
    public bool IsDestructive => Confirmation is not null;
}
