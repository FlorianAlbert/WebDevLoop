using WebDevLoop.Core.Agents;

namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>One running Copilot runtime (an SDK client and its runtime process). Disposing stops it; session state persists.</summary>
internal interface ICopilotRuntime : IAsyncDisposable
{
    /// <summary>Lists Models using the supplied signed-in user's token, without a session or SDK cache.</summary>
    Task<IReadOnlyList<ModelCatalogEntry>> ListModelsAsync(string gitHubToken, CancellationToken cancellationToken);

    /// <exception cref="CopilotAuthenticationException">The runtime rejected the Copilot credentials.</exception>
    Task<ICopilotAgentSession> CreateSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken);

    /// <summary>Resumes the persisted session <see cref="CopilotSessionSpec.SessionId"/> with the given tools and handlers.</summary>
    /// <exception cref="CopilotAuthenticationException">The runtime rejected the Copilot credentials.</exception>
    /// <exception cref="CopilotSessionNotFoundException">No persisted session has the requested id.</exception>
    Task<ICopilotAgentSession> ResumeSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken);
}
