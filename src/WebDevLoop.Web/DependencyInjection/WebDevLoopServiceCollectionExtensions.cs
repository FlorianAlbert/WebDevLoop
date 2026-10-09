using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Infrastructure.Skills;
using WebDevLoop.Web.Background;
using WebDevLoop.Web.GitHubAuth;

namespace WebDevLoop.Web.DependencyInjection;

public static class WebDevLoopServiceCollectionExtensions
{
    /// <summary>
    /// Composition root: configuration, adapters, Core workflow, REST API, UI and (unless
    /// <see cref="WorkflowWorkerOptions.Enabled"/> is false) the hosted workflow workers.
    /// </summary>
    /// <exception cref="WebDevLoopConfigurationException">The <c>WebDevLoop</c> configuration section is invalid.</exception>
    public static IServiceCollection AddWebDevLoop(this IServiceCollection services, IConfiguration configuration)
    {
        WebDevLoopOptions options = WebDevLoopOptions.Load(configuration);
        var skills = new BundledSkillsOptions();
        services.AddSingleton(options);
        services.AddSingleton(options.Workflow);
        services.AddWebDevLoopInfrastructure(options, skills);
        services.AddWebDevLoopOrchestration(options.Workflow, skills.Root);
        services.AddWebDevLoopApi();
        services.AddWebDevLoopApplicationServices();
        services.AddWebDevLoopDashboardUi();
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.TryAddSingleton<IAppInitializer, AppInitializer>();
        services.AddGitHubSignInReadinessSync();
        if (options.Workflow.Enabled)
        {
            services.AddWebDevLoopWorkers();
        }

        return services;
    }

    /// <summary>
    /// Hosted workers in start order; they stop in reverse order. The background work runner comes early so the other
    /// workers stop launching work before running work is cancelled and awaited, and the agent log flush comes first so it
    /// writes the logs of that work last.
    /// </summary>
    public static IServiceCollection AddWebDevLoopWorkers(this IServiceCollection services)
    {
        services.AddHostedService<AgentLogShutdownFlush>();
        services.AddHostedService(provider => provider.GetRequiredService<BackgroundWorkRunner>());
        services.AddHostedService<WorkflowEventSubscriptions>();
        services.AddHostedService<RecoveryWorker>();
        services.AddHostedService<OutboxDispatchWorker>();
        services.AddHostedService<MergeTrackingWorker>();
        services.AddHostedService<CopilotRuntimeMaintenanceWorker>();
        services.AddHostedService<OutboxRetentionWorker>();
        return services;
    }
}
