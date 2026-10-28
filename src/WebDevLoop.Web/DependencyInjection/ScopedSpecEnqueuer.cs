using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.DependencyInjection;

/// <summary>Isolates each batch attempt's unit of work and reports host readiness and unexpected failures.</summary>
internal sealed partial class ScopedSpecEnqueuer(
    IServiceScopeFactory scopes, DiagnosticReadiness readiness, ILogger<ScopedSpecEnqueuer> logger) : ISpecEnqueuer
{
    public async Task<EnqueueResult> EnqueueAsync(int repositoryId, int specIssueNumber, CancellationToken cancellationToken)
    {
        if (readiness.Current.Mode != ReadinessMode.Operational)
        {
            return new(EnqueueOutcome.Unavailable, Reason: "Workflow actions are disabled in diagnostic-only mode. Fix prerequisites on the Health page, then retry.");
        }

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISpecEnqueuer>()
                .EnqueueAsync(repositoryId, specIssueNumber, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogEnqueueFailed(logger, exception, repositoryId, specIssueNumber);
            return new(EnqueueOutcome.Failed, Reason: $"Could not queue #{specIssueNumber}. Check the queue before retrying. {exception.Message}");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Enqueueing issue {IssueNumber} in repository {RepositoryId} failed; continuing the selection.")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, int repositoryId, int issueNumber);
}
