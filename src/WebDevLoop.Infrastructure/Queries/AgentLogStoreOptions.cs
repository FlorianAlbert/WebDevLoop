namespace WebDevLoop.Infrastructure.Queries;

public sealed record AgentLogStoreOptions
{
    /// <summary>Retention cap: the oldest entries of a step beyond this count are deleted.</summary>
    public int MaxEntriesPerStep { get; init; } = 5000;

    /// <summary>Buffered entries are written as soon as this many are pending.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Buffered entries are written at the latest this long after the first of them arrived.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Most entries a single read returns; clients resume with the last sequence they received.</summary>
    public int PageSize { get; init; } = 1000;
}
