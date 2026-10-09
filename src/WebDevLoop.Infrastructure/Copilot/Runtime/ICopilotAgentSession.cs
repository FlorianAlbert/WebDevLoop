namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>An open Copilot session. Disposing releases it without deleting its persisted state.</summary>
internal interface ICopilotAgentSession : IAsyncDisposable
{
    /// <summary>Queues the prompt; progress and the end of the turn arrive through <see cref="CopilotSessionSpec.OnEvent"/>.</summary>
    /// <exception cref="CopilotAuthenticationException">The runtime rejected the Copilot credentials.</exception>
    Task SendAsync(string prompt, CancellationToken cancellationToken);

    Task AbortAsync(CancellationToken cancellationToken);
}
