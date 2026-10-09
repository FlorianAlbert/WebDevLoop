namespace WebDevLoop.Core.Events;

/// <summary>A state change other workers react to. Appended to the outbox in the same unit of work as the change.</summary>
public abstract record WorkflowEvent(DateTimeOffset OccurredAt);
