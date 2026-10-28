namespace WebDevLoop.Core.Orchestration.SpecQueue;

public enum EnqueueOutcome
{
    Queued,

    /// <summary>The spec issue already has an unfinished run in this repository; that run is returned.</summary>
    AlreadyQueued,

    RepositoryNotFound,

    /// <summary>Current GitHub issue state or opt-in does not permit queueing this spec.</summary>
    Ineligible,

    /// <summary>Required current GitHub information could not be fetched; nothing was queued.</summary>
    GitHubUnavailable,

    /// <summary>Another writer changed the queue first; nothing was saved and the caller may retry.</summary>
    ConcurrencyConflict,
}
