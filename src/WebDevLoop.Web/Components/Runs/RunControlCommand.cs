using WebDevLoop.Core.Orchestration.Control;

namespace WebDevLoop.Web.Components.Runs;

/// <param name="Key">Suffix of the button's <c>data-testid</c> (<c>control-{Key}</c>).</param>
/// <param name="IsAvailable">The run's state allows the command.</param>
/// <param name="Confirmation">Question asked before a destructive command runs; null runs it right away.</param>
public sealed record RunControlCommand(
    string Key,
    string Label,
    bool IsAvailable,
    string? Confirmation,
    Func<IRunControl, CancellationToken, Task<ControlResult>> Execute)
{
    public bool IsDestructive => Confirmation is not null;
}
