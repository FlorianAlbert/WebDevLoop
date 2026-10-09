using WebDevLoop.Infrastructure.Queries;

namespace WebDevLoop.Web.Background;

/// <summary>
/// Writes the agent log entries still buffered at shutdown while the container is usable. Registered before the background
/// work runner so it stops after the running agent work was cancelled and awaited.
/// </summary>
public sealed class AgentLogShutdownFlush(PersistentAgentLogStore logs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => logs.FlushAsync(cancellationToken);
}
