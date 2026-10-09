using WebDevLoop.Infrastructure.TestHost;

namespace WebDevLoop.Infrastructure.Tests.TestHost;

/// <summary>Port probe fake: listed ports are taken, and the application "starts" after a number of readiness probes.</summary>
internal sealed class FakePortProbe : IPortProbe
{
    public HashSet<int> Busy { get; } = [];

    /// <summary>Probes that fail before the application accepts connections; null means it never does.</summary>
    public int? ProbesUntilAccepting { get; set; }

    public int Probes { get; private set; }

    public bool IsFree(int port) => !Busy.Contains(port);

    public Task<bool> AcceptsConnectionsAsync(int port, CancellationToken cancellationToken)
    {
        Probes++;
        return Task.FromResult(ProbesUntilAccepting is { } until && Probes > until);
    }
}

internal sealed class FakeProcessTable(int currentProcessId, params HostProcess[] processes) : IProcessTable
{
    public int CurrentProcessId { get; } = currentProcessId;

    public IReadOnlyList<HostProcess> Snapshot() => processes;
}

/// <summary>Records kills; processes listed as gone have already exited.</summary>
internal sealed class RecordingTerminator : IProcessTerminator
{
    public HashSet<int> Gone { get; } = [];

    public List<int> Killed { get; } = [];

    public bool Kill(int processId)
    {
        if (Gone.Contains(processId))
        {
            return false;
        }

        Killed.Add(processId);
        return true;
    }
}
