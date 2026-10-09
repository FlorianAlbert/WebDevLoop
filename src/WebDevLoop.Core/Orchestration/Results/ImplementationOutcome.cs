namespace WebDevLoop.Core.Orchestration.Results;

public enum ImplementationOutcome
{
    Implemented,

    /// <summary>The implementer could not complete the ticket (e.g. contradictory spec); the app decides retry vs needs-attention.</summary>
    Blocked,
}
