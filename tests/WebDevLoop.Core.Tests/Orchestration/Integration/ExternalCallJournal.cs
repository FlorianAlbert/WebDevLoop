namespace WebDevLoop.Core.Tests.Orchestration.Integration;

/// <summary>
/// Records the side-effecting Git/GitHub calls of an integration saga and simulates a process crash: after the configured
/// call (or save, see <see cref="CrashingUnitOfWork"/>) the "process" is down and every later save fails as well, so
/// nothing after the crash point is persisted. <see cref="Restart"/> brings it back for the resumed run.
/// </summary>
internal sealed class ExternalCallJournal
{
    private readonly Dictionary<string, TaskCompletionSource> _holds = [];

    /// <summary>Completed side-effecting calls in order, across crashes and restarts.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Side-effecting calls that were entered (before any <see cref="Hold"/> released them).</summary>
    public List<string> Started { get; } = [];

    public int? CrashAfterCall { get; set; }

    public bool IsDown { get; private set; }

    /// <summary>Runs after a call completed and before any crash, e.g. to tamper with GitHub state.</summary>
    public Action<string>? AfterCall { get; set; }

    /// <summary>The next call named <paramref name="call"/> waits until the returned source is completed.</summary>
    public TaskCompletionSource Hold(string call)
    {
        var release = new TaskCompletionSource();
        _holds[call] = release;
        return release;
    }

    public async Task BeforeAsync(string call)
    {
        ThrowIfDown();
        Started.Add(call);
        if (_holds.Remove(call, out TaskCompletionSource? release))
        {
            await release.Task;
        }
    }

    public void Record(string call)
    {
        Calls.Add(call);
        AfterCall?.Invoke(call);
        if (Calls.Count == CrashAfterCall)
        {
            Crash($"after {call}");
        }
    }

    public void Crash(string point)
    {
        IsDown = true;
        throw new SimulatedCrashException(point);
    }

    public void ThrowIfDown()
    {
        if (IsDown)
        {
            throw new SimulatedCrashException("the process is down");
        }
    }

    public void Restart()
    {
        IsDown = false;
        CrashAfterCall = null;
    }
}

internal sealed class SimulatedCrashException(string point) : Exception($"Simulated crash {point}.");
