namespace WebDevLoop.Core.Domain;

public sealed class RunEvent
{
    private RunEvent()
    {
        Type = string.Empty;
        PayloadJson = string.Empty;
    }

    public long Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public TicketRunId? TicketRunId { get; private set; }

    public string Type { get; private set; }

    public string PayloadJson { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static RunEvent Create(RunId specRunId, TicketRunId? ticketRunId, string type, string payloadJson, DateTimeOffset at) => new()
    {
        SpecRunId = specRunId,
        TicketRunId = ticketRunId,
        Type = type,
        PayloadJson = payloadJson,
        OccurredAt = at,
    };
}
