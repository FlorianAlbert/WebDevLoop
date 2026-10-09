namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Status of an exploration or implementation report (<c>completed</c> / <c>blocked</c>).</summary>
public enum ReportStatus
{
    Completed,

    /// <summary>The agent could not do the work as specified; the reason is in the summary. The app decides retry vs needs-attention.</summary>
    Blocked,
}
