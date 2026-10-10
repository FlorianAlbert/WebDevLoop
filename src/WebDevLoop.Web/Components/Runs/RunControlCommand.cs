using WebDevLoop.Core.Orchestration.Control;

namespace WebDevLoop.Web.Components.Runs;

/// <param name="Key">Suffix of the button's <c>data-testid</c> (<c>control-{Key}</c>).</param>
/// <param name="IsAvailable">The run's state allows the command.</param>
/// <param name="UnavailableReason">Why the command is disabled while <paramref name="IsAvailable"/> is false.</param>
/// <param name="Confirmation">Question asked before a destructive command runs, naming what it affects; null runs it right away.</param>
/// <param name="Consequence">One plain sentence under the button: what pressing it causes.</param>
/// <param name="Impact">The tickets or the number of tickets this press affects, shown after the consequence.</param>
public sealed record RunControlCommand(
    string Key,
    string Label,
    bool IsAvailable,
    string? UnavailableReason,
    string? Confirmation,
    Func<IRunControl, CancellationToken, Task<ControlResult>> Execute,
    string? Consequence = null,
    string? Impact = null)
{
    public bool IsDestructive => Confirmation is not null;
}
