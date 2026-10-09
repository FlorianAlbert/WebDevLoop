using GitHub.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal sealed class SdkCopilotSession(CopilotSession session) : ICopilotAgentSession
{
    public Task SendAsync(string prompt, CancellationToken cancellationToken) =>
        SdkCalls.RunAsync(() => session.SendAsync(new MessageOptions { Prompt = prompt }, cancellationToken));

    public Task AbortAsync(CancellationToken cancellationToken) => session.AbortAsync(cancellationToken);

    /// <summary>Releases the session; its persisted state stays available for resume.</summary>
    public ValueTask DisposeAsync() => session.DisposeAsync();
}
