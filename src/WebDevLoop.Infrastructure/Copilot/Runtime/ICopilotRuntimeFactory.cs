namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>Starts Copilot runtimes. The SDK adapter is the only production implementation; tests use fakes.</summary>
internal interface ICopilotRuntimeFactory
{
    Task<ICopilotRuntime> StartAsync(CopilotRuntimeLaunch launch, CancellationToken cancellationToken);
}
