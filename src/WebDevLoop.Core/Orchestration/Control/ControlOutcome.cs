namespace WebDevLoop.Core.Orchestration.Control;

public enum ControlOutcome
{
    Applied,
    NotFound,

    /// <summary>The run's current state does not allow the action (e.g. retrying a run that does not need attention).</summary>
    NotAllowed,

    /// <summary>Retrying would make the spec active, but every active-spec slot of its repository is taken.</summary>
    NoActiveSlot,

    /// <summary>Another writer changed the run first (or a merge of the repository is in progress); nothing was changed.</summary>
    ConcurrencyConflict,
}
