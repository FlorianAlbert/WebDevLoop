using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <summary>Lightweight in-process push signal that a step's agent log has new readable entries, so viewers need not poll.</summary>
public interface IAgentLogNotifications
{
    /// <summary>
    /// <paramref name="onEntriesAvailable"/> is called, possibly on any thread and more than once per read, after new entries of the
    /// step became readable. It must be cheap and must not throw; dispose the returned handle to unsubscribe.
    /// </summary>
    IDisposable Subscribe(StepRunId stepRunId, Action onEntriesAvailable);
}
