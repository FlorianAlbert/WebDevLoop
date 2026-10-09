namespace WebDevLoop.Core.Domain;

/// <summary>Ownership of a reserved port and tester process; expired leases are cleaned up on recovery.</summary>
public sealed class TestLease : VersionedEntity
{
    private TestLease()
    {
        WorkspacePath = string.Empty;
    }

    public long Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public int Port { get; private set; }

    public string WorkspacePath { get; private set; }

    public int? ProcessId { get; private set; }

    public DateTimeOffset AcquiredAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public bool IsActive => ReleasedAt is null;

    public static TestLease Acquire(RunId specRunId, int port, string workspacePath, DateTimeOffset at, TimeSpan timeToLive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, TestPortRange.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, TestPortRange.MaxPort);

        return new()
        {
            SpecRunId = specRunId,
            Port = port,
            WorkspacePath = workspacePath,
            AcquiredAt = at,
            ExpiresAt = at + timeToLive,
        };
    }

    public void AttachProcess(int processId) => ProcessId = processId;

    public bool IsExpiredAt(DateTimeOffset now) => IsActive && now >= ExpiresAt;

    public void Release(DateTimeOffset at) => ReleasedAt = at;
}
