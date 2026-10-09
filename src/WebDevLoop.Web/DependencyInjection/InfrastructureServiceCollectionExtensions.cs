using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Infrastructure.Copilot;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Infrastructure.Git;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.GitHub.Issues;
using WebDevLoop.Infrastructure.GitHub.Pulls;
using WebDevLoop.Infrastructure.GitHub.Stacks;
using WebDevLoop.Infrastructure.Management;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Infrastructure.Queries;
using WebDevLoop.Infrastructure.Runtime;
using WebDevLoop.Infrastructure.Skills;
using WebDevLoop.Infrastructure.TestHost;
using WebDevLoop.Web.Resources.Prompts;

namespace WebDevLoop.Web.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the adapters behind the Core ports: clock and ids, embedded defaults, SQLite persistence with the outbox and
    /// read models, GitHub (App tokens with PAT fallback, issues, PRs and stacks), the git workspace, the Copilot agent
    /// runner and runtime pool, the tester app host, the prerequisite checks and the persisted UI selection. Adapters that
    /// depend on the global settings are created after <see cref="StartupSettings"/> were resolved at startup.
    /// </summary>
    public static IServiceCollection AddWebDevLoopInfrastructure(this IServiceCollection services, WebDevLoopOptions options, BundledSkillsOptions skills)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(skills);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidIdGenerator>();
        services.TryAddSingleton<StartupSettings>();
        services.TryAddSingleton<IDefaultPromptTemplates, EmbeddedDefaultPromptTemplates>();
        services.TryAddSingleton(provider => DefaultSettings.Create(provider.GetRequiredService<IDefaultPromptTemplates>(), options.ResolvedDataDirectory));
        services.TryAddScoped<IWorkspaceRootProvider, StartupWorkspaceRootProvider>();
        services.TryAddSingleton<ICurrentRepositorySelection>(_ => new FileCurrentRepositorySelection(options.UiStatePath));

        services.AddPersistence(options.ConnectionString);
        services.AddEventing();
        services.TryAddSingleton(options.AgentLogs);
        services.AddQueries();
        services.TryAddScoped<OutboxRetention>();

        services.AddGitHub(options.GitHub);
        services.AddGitWorkspace();
        services.AddCopilot(options.Copilot, skills);
        services.AddTestHost();
        services.AddPrerequisites(provider => PrerequisitesFor(provider, options), options.ConnectionString, new BundledSkillsCatalog(skills));
        return services;
    }

    private static void AddGitHub(this IServiceCollection services, GitHubConnectionOptions github)
    {
        services.TryAddSingleton(new GitHubApiConnection(github.ApiBaseUrl));
        services.TryAddSingleton(provider => new GitHubAuthOptions
        {
            ApiBaseUrl = github.ApiBaseUrl,
            AppClientId = NullIfEmpty(github.AppClientId),
            AppPrivateKeyPem = NullIfEmpty(github.ResolvePrivateKeyPem()),
            UserToken = NullIfEmpty(github.UserToken),
            PatFallbackEnabled = provider.GetRequiredService<StartupSettings>().Current.PatFallbackEnabled,
        });
        services.TryAddSingleton<ITokenProvider>(provider => new GitHubTokenProvider(
            provider.GetRequiredService<GitHubApiConnection>().Client,
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<GitHubAuthOptions>()));
        services.TryAddSingleton<IGitCredentialSource, GitCredentialSource>();
        services.TryAddSingleton<IGitHubIssues>(provider => new GitHubIssues(
            provider.GetRequiredService<GitHubApiConnection>().Client,
            provider.GetRequiredService<ITokenProvider>()));

        // gh stack is only reached when the stack REST endpoints answer 404 (both modes); the check reports whether it is installed.
        services.TryAddSingleton<IGitHubPullsAndStacks>(provider => new GitHubPullsAndStacks(
            provider.GetRequiredService<GitHubApiConnection>().Client,
            provider.GetRequiredService<ITokenProvider>(),
            new GitHubPullsOptions
            {
                ApiBaseUrl = github.ApiBaseUrl,
                GraphQlUrl = github.GraphQlUrl,
                AllowUserTokenFallback = provider.GetRequiredService<GitHubAuthOptions>().PatFallbackEnabled,
            },
            new ProcessGhCommandRunner(github.GhExecutable)));
    }

    private static void AddGitWorkspace(this IServiceCollection services) =>
        services.TryAddSingleton<IGitWorkspace>(provider =>
        {
            EffectiveSettings global = provider.GetRequiredService<StartupSettings>().Current;
            return new GitWorkspace(
                new GitWorkspaceOptions { WorkspaceRoot = global.WorkspaceRootDirectory, AllowUserTokenFallback = global.PatFallbackEnabled },
                provider.GetRequiredService<IGitCredentialSource>(),
                provider.GetRequiredService<IClock>());
        });

    /// <summary>The runtime pool is disposed with the container on shutdown, which stops the Copilot runtimes.</summary>
    private static void AddCopilot(this IServiceCollection services, CopilotConnectionOptions copilot, BundledSkillsOptions skills)
    {
        services.TryAddSingleton(provider => CopilotAgentServices.Create(
            provider.GetRequiredService<ITokenProvider>(),
            provider.GetRequiredService<IAgentLogSink>(),
            provider.GetRequiredService<IClock>(),
            new CopilotRuntimeOptions
            {
                BaseDirectory = provider.GetRequiredService<StartupSettings>().Current.CopilotBaseDirectory,
                CliPath = NullIfEmpty(copilot.CliPath),
                TokenRefreshSkew = copilot.TokenRefreshSkew,
                IdleTimeout = copilot.IdleTimeout,
            },
            skills));
        services.TryAddSingleton<IAgentRunner>(provider => provider.GetRequiredService<CopilotAgentServices>().Runner);
        services.TryAddSingleton<ICopilotRuntimePool>(provider => provider.GetRequiredService<CopilotAgentServices>().RuntimePool);
    }

    /// <summary>appsettings.json lists every key with an empty value; empty means "not configured".</summary>
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static PrerequisiteOptions PrerequisitesFor(IServiceProvider provider, WebDevLoopOptions options)
    {
        EffectiveSettings global = provider.GetRequiredService<StartupSettings>().Current;
        return new PrerequisiteOptions
        {
            WorkspaceRoot = global.WorkspaceRootDirectory,
            GitHubAuth = provider.GetRequiredService<GitHubAuthOptions>(),
            CopilotCliPath = NullIfEmpty(options.Copilot.CliPath),
            TestPortRange = global.TestPortRange,
            GhStackMode = options.GitHub.GhStackMode,
            GitExecutable = options.Tools.GitExecutable,
            GhExecutable = options.GitHub.GhExecutable,
            PlaywrightCliExecutable = options.Tools.PlaywrightCliExecutable,
        };
    }
}

/// <summary>One pooled HTTP connection to the GitHub API shared by the token provider and the adapters (they set headers per request).</summary>
public sealed class GitHubApiConnection(string apiBaseUrl) : IDisposable
{
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    public HttpClient Client { get; } = new(new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime })
    {
        BaseAddress = new Uri(apiBaseUrl),
    };

    public void Dispose() => Client.Dispose();
}
