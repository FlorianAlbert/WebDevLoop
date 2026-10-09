namespace WebDevLoop.Core.Events;

/// <summary>A delivered event; subscribers deduplicate redeliveries by <paramref name="MessageId"/> (the outbox row id).</summary>
public sealed record EventEnvelope(long MessageId, WorkflowEvent Event);
