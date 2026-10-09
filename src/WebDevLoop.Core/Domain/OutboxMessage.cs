namespace WebDevLoop.Core.Domain;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        Type = string.Empty;
        PayloadJson = string.Empty;
    }

    public long Id { get; private set; }

    public string Type { get; private set; }

    public string PayloadJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public bool IsPending => DispatchedAt is null;

    public static OutboxMessage Create(string type, string payloadJson, DateTimeOffset at) => new()
    {
        Type = type,
        PayloadJson = payloadJson,
        CreatedAt = at,
    };

    public void MarkDispatched(DateTimeOffset at) => DispatchedAt = at;

    public void RecordFailure(string error)
    {
        Attempts++;
        LastError = error;
    }
}
