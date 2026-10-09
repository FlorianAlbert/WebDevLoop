namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>An audited user command on a spec or ticket run.</summary>
public enum ControlAction
{
    Retry,
    Skip,
    Abort,
}
