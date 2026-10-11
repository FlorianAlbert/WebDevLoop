using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Web.Background;

namespace WebDevLoop.Web.GitHubAuth;

/// <summary>
/// Re-fetches the Model Catalog and evaluates prerequisites after every sign-in change (including an expired sign-in),
/// so readiness follows the signed-in account without a manual re-check.
/// </summary>
public sealed partial class GitHubSignInReadinessSync(
    IGitHubSignInState signIn,
    ModelCatalogReadiness models,
    ILogger<GitHubSignInReadinessSync> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        signIn.Changed += OnChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        signIn.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            await models.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogRefreshFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-evaluating the prerequisites after a GitHub sign-in change failed.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
