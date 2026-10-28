namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>An audited command on a spec or ticket run: a user's, or WebDevLoop's own retry after a remediation.</summary>
public enum ControlAction
{
    Retry,
    Abort = 2,
    AutoRetry = 3,
}
