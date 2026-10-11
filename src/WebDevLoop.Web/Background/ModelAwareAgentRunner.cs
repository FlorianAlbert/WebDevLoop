using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Background;

/// <summary>Confirms Model loss after failed agent launches or turns and revalidates scheduler readiness.</summary>
public sealed partial class ModelAwareAgentRunner(
    IAgentRunner agents, ModelCatalogReadiness models, ILogger<ModelAwareAgentRunner> logger) : IAgentRunner
{
    public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        RunAsync(request, () => agents.StartAsync(request, cancellationToken), cancellationToken);

    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        RunAsync(request, () => agents.ResumeAsync(request, cancellationToken), cancellationToken);

    public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken) =>
        agents.AbortAsync(sessionId, cancellationToken);

    private async Task<AgentRunResult> RunAsync(AgentRunRequest request, Func<Task<AgentRunResult>> launch, CancellationToken cancellationToken)
    {
        AgentRunResult result;
        try
        {
            result = await launch();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            LogLaunchFailed(logger, exception, request.StepRunId.Value, request.Settings.Model);
            result = AgentRunResult.NotReported(AgentRunOutcome.Failed, exception.Message);
        }

        // The SDK has no typed Model error at this boundary; refresh-and-compare avoids guessing from error wording.
        if (result.Outcome == AgentRunOutcome.Failed
            && await models.ConfirmModelUnavailableAsync(request.Settings.Model, cancellationToken))
        {
            return AgentRunResult.NotReported(AgentRunOutcome.ModelUnavailable,
                $"Model '{request.Settings.Model}' for {request.Role} in {request.Repository} is missing from the freshly fetched Model Catalog. "
                + $"Agent failure: {result.FailureReason}");
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent step {StepRunId} using Model '{Model}' failed unexpectedly.")]
    private static partial void LogLaunchFailed(ILogger logger, Exception exception, string stepRunId, string model);
}
