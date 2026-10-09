namespace WebDevLoop.Core.Queries;

public sealed record RunEventView(long Id, string SpecRunId, string? TicketRunId, string Type, string PayloadJson, DateTimeOffset OccurredAt);
