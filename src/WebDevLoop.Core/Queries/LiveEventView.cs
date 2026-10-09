namespace WebDevLoop.Core.Queries;

/// <summary>Flat projection of a dispatched workflow event for live subscribers. <see cref="Status"/> is the new status for status-change events.</summary>
public sealed record LiveEventView(
    long MessageId,
    string Type,
    string? SpecRunId,
    string? TicketRunId,
    string? StepRunId,
    string? Status,
    DateTimeOffset OccurredAt);
