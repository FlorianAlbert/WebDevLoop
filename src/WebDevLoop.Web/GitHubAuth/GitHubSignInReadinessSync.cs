using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.GitHubAuth;

/// <summary>
/// Refreshes the prerequisite evaluation after every sign-in change (including a sign-in that expired during a token
/// refresh), so the GitHub authentication check and the readiness mode follow the sign-in without a manual re-check.
/// </summary>
public sealed partial class GitHubSignInReadinessSync(
    IGitHubSignInState signIn,
    DiagnosticReadiness readiness,
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
            await readiness.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogRefreshFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-evaluating the prerequisites after a GitHub sign-in change failed.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
