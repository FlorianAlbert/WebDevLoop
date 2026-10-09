namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>What skipping a ticket means for the tickets it blocks (the user's explicit skip policy).</summary>
public enum SkipDependents
{
    /// <summary>The skipped ticket counts as done: its dependents may start on an integration branch without its change.</summary>
    Unblock,

    /// <summary>Every not-yet-started ticket that (transitively) depends on the skipped ticket is skipped as well.</summary>
    Skip,
}
