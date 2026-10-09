namespace WebDevLoop.Core.Queries;

/// <summary>Where a spec run's PR stack stands with the human merge.</summary>
public enum MergeState
{
    /// <summary>The stack is not ready for review yet (or the run needs attention before it got there).</summary>
    NotReady,

    /// <summary>The stack is ready for review and tracked until it is merged into trunk.</summary>
    Awaiting,

    /// <summary>The stack was merged and trunk contains its top layer.</summary>
    Merged,

    /// <summary>Merge tracking gave up: the stack was closed unmerged, or trunk never received its top layer.</summary>
    Closed,

    /// <summary>The run completed without publishing pull requests (the integration branch was reported instead).</summary>
    CompletedWithoutPullRequests,

    Aborted,
}
