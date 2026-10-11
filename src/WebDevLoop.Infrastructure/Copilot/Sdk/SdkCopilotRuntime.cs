using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using WebDevLoop.Core.Agents;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal sealed class SdkCopilotRuntime(CopilotClient client) : ICopilotRuntime
{
#pragma warning disable GHCP001 // The typed model RPC is experimental; it supports explicit session identity and uncached refreshes.
    public async Task<IReadOnlyList<ModelCatalogEntry>> ListModelsAsync(string gitHubToken, CancellationToken cancellationToken)
    {
        // ListModelsAsync caches in the SDK and uses runtime authentication; the RPC supports an explicit user token.
        ModelList listing = await SdkCalls.RunAsync(() =>
            client.Rpc.Models.ListAsync(gitHubToken: gitHubToken, cancellationToken: cancellationToken));
        return listing.Models.Select(model => new ModelCatalogEntry(
            model.Id, model.Name, model.SupportedReasoningEfforts?.ToArray() ?? [])).ToArray();
    }
#pragma warning restore GHCP001

    public async Task<ICopilotAgentSession> CreateSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken)
    {
        SessionConfig config = SdkSessionConfigFactory.CreateSession(spec);
        return new SdkCopilotSession(await SdkCalls.RunAsync(() => client.CreateSessionAsync(config, cancellationToken)));
    }

    /// <exception cref="CopilotSessionNotFoundException">No persisted session has the requested id.</exception>
    public async Task<ICopilotAgentSession> ResumeSessionAsync(CopilotSessionSpec spec, CancellationToken cancellationToken)
    {
        string sessionId = spec.SessionId.Value;
        if (await SdkCalls.RunAsync(() => client.GetSessionMetadataAsync(sessionId, cancellationToken)) is null)
        {
            throw new CopilotSessionNotFoundException(sessionId);
        }

        ResumeSessionConfig config = SdkSessionConfigFactory.CreateResume(spec);
        return new SdkCopilotSession(await SdkCalls.RunAsync(() => client.ResumeSessionAsync(sessionId, config, cancellationToken)));
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
