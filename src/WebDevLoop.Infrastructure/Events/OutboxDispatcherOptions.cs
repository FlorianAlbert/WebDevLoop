namespace WebDevLoop.Infrastructure.Events;

public sealed record OutboxDispatcherOptions
{
    public const int DefaultBatchSize = 100;

    public int BatchSize { get; init; } = DefaultBatchSize;
}
