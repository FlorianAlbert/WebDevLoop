using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>A leased runtime plus the Copilot token resolved for the acquiring session. Disposing releases the lease.</summary>
internal sealed record CopilotRuntimeHandle(CopilotRuntimeLease Lease, ICopilotRuntime Runtime, GitHubAccessToken Token) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Lease.DisposeAsync();
}
