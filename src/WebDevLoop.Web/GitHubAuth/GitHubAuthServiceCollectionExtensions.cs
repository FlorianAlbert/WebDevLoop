using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.GitHubAuth;

public static class GitHubAuthServiceCollectionExtensions
{
    private const string ApplicationName = "WebDevLoop";

    /// <summary>
    /// Persists the data-protection keys under the data directory (so the stored GitHub sign-in survives restarts) and
    /// registers the encrypted <see cref="IGitHubCredentialStore"/>.
    /// </summary>
    public static IServiceCollection AddGitHubCredentialProtection(this IServiceCollection services, WebDevLoopOptions options)
    {
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysDirectory));
        services.TryAddSingleton<IGitHubCredentialStore>(provider => new DataProtectedGitHubCredentialStore(
            provider.GetRequiredService<IDataProtectionProvider>(),
            options.GitHubCredentialsPath,
            options.DataProtectionKeysDirectory,
            provider.GetRequiredService<ILogger<DataProtectedGitHubCredentialStore>>()));
        return services;
    }

    /// <summary>Re-evaluates the prerequisites whenever the user signs in or out, so the workflow starts or stops gating on it.</summary>
    public static IServiceCollection AddGitHubSignInReadinessSync(this IServiceCollection services)
    {
        services.AddHostedService<GitHubSignInReadinessSync>();
        return services;
    }
}
