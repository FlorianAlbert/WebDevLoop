using GitHub.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal sealed class SdkCopilotRuntime(CopilotClient client) : ICopilotRuntime
{
    public async Task<ICopilotAgentSession> CreateSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken)
    {
        SessionConfig config = SdkSessionConfigFactory.CreateSession(spec);
        return new SdkCopilotSession(await SdkCalls.RunAsync(() => client.CreateSessionAsync(config, cancellationToken)));
    }

    public async Task<ICopilotAgentSession> ResumeSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken)
    {
        ResumeSessionConfig config = SdkSessionConfigFactory.CreateResume(spec);
        return new SdkCopilotSession(await SdkCalls.RunAsync(() => client.ResumeSessionAsync(spec.SessionId.Value, config, cancellationToken)));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await client.StopAsync();
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
