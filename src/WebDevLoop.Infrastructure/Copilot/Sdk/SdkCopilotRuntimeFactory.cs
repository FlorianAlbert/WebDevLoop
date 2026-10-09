using GitHub.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

/// <summary>Starts one SDK <see cref="CopilotClient"/> (and its runtime process) per launch.</summary>
internal sealed class SdkCopilotRuntimeFactory : ICopilotRuntimeFactory
{
    public async Task<ICopilotRuntime> StartAsync(CopilotRuntimeLaunch launch, CancellationToken cancellationToken)
    {
        var client = new CopilotClient(SdkClientOptionsFactory.Create(launch));
        try
        {
            await SdkCalls.RunAsync(() => client.StartAsync(cancellationToken));
            return new SdkCopilotRuntime(client);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}
