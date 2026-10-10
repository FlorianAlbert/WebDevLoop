namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>An audited command on a spec or ticket run: a user's, or (<see cref="AutoRetry"/>, <see cref="AutoSkip"/>) WebDevLoop's own after a remediation.</summary>
public enum ControlAction
{
    Retry,
    Skip,
    Abort,
    AutoRetry,
    AutoSkip,
}
