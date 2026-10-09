using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Tests.Copilot.Fakes;

/// <summary>In-memory stand-in for the SDK: records launches and sessions and scripts what each prompt does.</summary>
internal sealed class FakeCopilotRuntimeFactory : ICopilotRuntimeFactory
{
    public List<FakeCopilotRuntime> Runtimes { get; } = [];

    /// <summary>What a sent prompt does; the default ends the turn without a report.</summary>
    public Func<FakeTurn, Task> OnSend { get; set; } = turn =>
    {
        turn.Raise(CopilotSessionEventKind.Idle);
        return Task.CompletedTask;
    };

    /// <summary>Optional failure when a session is created or resumed on a runtime (by runtime index).</summary>
    public Func<int, Exception?> OnOpenSession { get; set; } = _ => null;

    public Task<ICopilotRuntime> StartAsync(CopilotRuntimeLaunch launch, CancellationToken cancellationToken)
    {
        var runtime = new FakeCopilotRuntime(this, Runtimes.Count, launch);
        Runtimes.Add(runtime);
        return Task.FromResult<ICopilotRuntime>(runtime);
    }
}

internal sealed class FakeCopilotRuntime(FakeCopilotRuntimeFactory factory, int index, CopilotRuntimeLaunch launch) : ICopilotRuntime
{
    public int Index { get; } = index;

    public CopilotRuntimeLaunch Launch { get; } = launch;

    public List<CopilotSessionSpec> Created { get; } = [];

    public List<CopilotSessionSpec> Resumed { get; } = [];

    public List<FakeCopilotSession> Sessions { get; } = [];

    public bool IsDisposed { get; private set; }

    public Task<ICopilotAgentSession> CreateSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken)
    {
        Created.Add(spec);
        return Open(spec);
    }

    public Task<ICopilotAgentSession> ResumeSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken)
    {
        Resumed.Add(spec);
        return Open(spec);
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private Task<ICopilotAgentSession> Open(CopilotSessionSpec spec)
    {
        if (factory.OnOpenSession(Index) is { } failure)
        {
            return Task.FromException<ICopilotAgentSession>(failure);
        }

        var session = new FakeCopilotSession(factory, this, spec);
        Sessions.Add(session);
        return Task.FromResult<ICopilotAgentSession>(session);
    }
}

internal sealed class FakeCopilotSession(FakeCopilotRuntimeFactory factory, FakeCopilotRuntime runtime, CopilotSessionSpec spec) : ICopilotAgentSession
{
    public CopilotSessionSpec Spec { get; } = spec;

    public List<string> Prompts { get; } = [];

    public int AbortCount { get; private set; }

    public bool IsDisposed { get; private set; }

    public Task SendAsync(string prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return factory.OnSend(new FakeTurn(runtime, Spec, prompt));
    }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        AbortCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed record FakeTurn(FakeCopilotRuntime Runtime, CopilotSessionSpec Spec, string Prompt)
{
    public void Raise(CopilotSessionEventKind kind, string text = "", bool isAuthenticationFailure = false) =>
        Spec.OnEvent(new CopilotSessionEvent(kind, text, isAuthenticationFailure));

    /// <summary>Calls the report tool the way the runtime would, then ends the turn.</summary>
    public string Report(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        string result = Spec.ReportTool.Invoke(document.RootElement.Clone());
        Raise(CopilotSessionEventKind.Idle);
        return result;
    }
}
