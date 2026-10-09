using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Web.Background;

namespace WebDevLoop.Web.DependencyInjection;

public static class OrchestrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Core workflow: settings resolution, the spec queue and preparation, frontier dispatch, review/fix loop,
    /// integration saga, parent review, tester, completion and merge tracking, recovery, the event handlers, and the
    /// background launchers. Runners and handlers are scoped (one unit of work per launch or event); gates, the process
    /// boot time and options are singletons. Requires the persistence, eventing and adapter registrations.
    /// </summary>
    public static IServiceCollection AddWebDevLoopOrchestration(this IServiceCollection services, WorkflowWorkerOptions workflow, string skillsRoot)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        services.AddSettingsResolution();
        services.AddOrchestrationOptions(workflow, skillsRoot);
        services.AddLaunchers();

        services.TryAddScoped<GlobalSettingsSeeder>();
        services.TryAddScoped<SpecQueueService>();
        services.TryAddScoped<SpecQueueScheduler>();
        services.TryAddScoped<SpecSnapshotter>();
        services.TryAddScoped<SpecExplorer>();
        services.TryAddScoped<SpecPreparationService>();
        services.TryAddSingleton<IAppDirectoryProvisioner, FileSystemAppDirectoryProvisioner>();

        services.TryAddSingleton<ImplementerCapacityGate>();
        services.TryAddScoped<ImplementerCapacity>();
        services.TryAddScoped<TicketDispatcher>();
        services.TryAddScoped<TicketImplementationRunner>();
        services.TryAddScoped<FrontierService>();

        services.TryAddScoped<TwoAxisReviewRunner>();
        services.TryAddScoped<ReviewFixRunner>();
        services.TryAddScoped<TicketReviewLoop>();

        services.TryAddSingleton<RepositoryIntegrationGate>();
        services.TryAddScoped<ConflictResolutionRunner>();
        services.TryAddScoped<IntegrationSagaSteps>();
        services.TryAddScoped<IntegrationSagaRunner>();

        services.TryAddScoped<FindingTicketIssuer>();
        services.TryAddScoped<ParentReviewStarter>();
        services.TryAddScoped<ParentSpecReviewRunner>();
        services.TryAddScoped<TesterAttemptRunner>();
        services.TryAddScoped<SpecTestRunner>();
        services.TryAddScoped<SpecCompletionService>();
        services.TryAddScoped<MergeTrackingService>();

        services.AddRecovery();
        services.AddEventHandlers();
        return services;
    }

    private static void AddSettingsResolution(this IServiceCollection services)
    {
        services.TryAddSingleton<PromptRenderer>();
        services.TryAddSingleton(provider => new SettingsResolver(provider.GetRequiredService<EffectiveSettings>()));
        services.TryAddScoped<IEffectiveSettingsProvider, PersistedEffectiveSettingsProvider>();
    }

    private static void AddOrchestrationOptions(this IServiceCollection services, WorkflowWorkerOptions workflow, string skillsRoot)
    {
        services.TryAddSingleton(new SpecPreparationOptions(skillsRoot, workflow.ExplorationEnabled));
        services.TryAddSingleton(new TicketExecutionOptions(skillsRoot));
        services.TryAddSingleton(new ReviewOptions(skillsRoot));
        services.TryAddSingleton(new IntegrationOptions(skillsRoot));
        services.TryAddSingleton(new TestingOptions(skillsRoot) { AppStartupTimeout = workflow.TesterAppStartupTimeout });
        services.TryAddSingleton(new ReadyAndMergeOptions { TrunkContainmentTimeout = workflow.TrunkContainmentTimeout });
        services.TryAddSingleton(new ExternalReconciliationOptions(workflow.ParkedIntegrationRetryInterval));
        services.TryAddSingleton(new AgentStepRecoveryOptions(workflow.StallGracePeriod));
    }

    /// <summary>One launcher instance serves every workflow phase; each launch runs in its own DI scope.</summary>
    private static void AddLaunchers(this IServiceCollection services)
    {
        services.TryAddSingleton<BackgroundWorkRunner>();
        services.TryAddSingleton<BackgroundWorkflowLaunchers>();
        services.TryAddSingleton<IPreparationLauncher>(provider => provider.GetRequiredService<BackgroundWorkflowLaunchers>());
        services.TryAddSingleton<IImplementationLauncher>(provider => provider.GetRequiredService<BackgroundWorkflowLaunchers>());
        services.TryAddSingleton<IReviewLoopLauncher>(provider => provider.GetRequiredService<BackgroundWorkflowLaunchers>());
        services.TryAddSingleton<IIntegrationLauncher>(provider => provider.GetRequiredService<BackgroundWorkflowLaunchers>());
        services.TryAddSingleton<IParentReviewLauncher>(provider => provider.GetRequiredService<BackgroundWorkflowLaunchers>());
        services.TryAddSingleton<ITestingLauncher>(provider => provider.GetRequiredService<BackgroundWorkflowLaunchers>());
        services.TryAddSingleton<ICompletionLauncher>(provider => provider.GetRequiredService<BackgroundWorkflowLaunchers>());
        services.TryAddSingleton(provider => new AgentStepRecoveryLaunchers(
            provider.GetRequiredService<IImplementationLauncher>(),
            provider.GetRequiredService<IReviewLoopLauncher>(),
            provider.GetRequiredService<IIntegrationLauncher>(),
            provider.GetRequiredService<IParentReviewLauncher>(),
            provider.GetRequiredService<ITestingLauncher>()));
    }

    private static void AddRecovery(this IServiceCollection services)
    {
        services.TryAddSingleton(provider => ProcessBoot.Now(provider.GetRequiredService<IClock>()));
        services.TryAddSingleton<SchedulerStartGate>();

        services.TryAddScoped<IntegrationBranchReconciler>();
        services.TryAddScoped<TicketGraphReconciler>();
        services.TryAddScoped<FindingIssuanceReconciler>();
        services.TryAddScoped<WorktreeReconciler>();
        services.TryAddScoped<IntegrationSagaReconciler>();
        services.TryAddScoped<StackBaseReconciler>();
        services.TryAddScoped<ExternalStateReconciler>();
        services.TryAddScoped<IExternalStateRecovery>(provider => provider.GetRequiredService<ExternalStateReconciler>());

        services.TryAddScoped<OrphanedTestLeaseStopper>();
        services.TryAddScoped<InterruptedStepFinisher>();
        services.TryAddScoped<StalledWorkRelauncher>();
        services.TryAddScoped<AgentStepRecoveryService>();
        services.TryAddScoped<IAgentStepRecovery>(provider => provider.GetRequiredService<AgentStepRecoveryService>());

        services.TryAddScoped<SpecQueueRecomputation>();
        services.TryAddScoped<ISpecQueueRecomputation>(provider => provider.GetRequiredService<SpecQueueRecomputation>());
        services.TryAddScoped<RecoveryCoordinator>();
    }

    private static void AddEventHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<SpecQueueEventHandler>();
        services.TryAddScoped<PreparationEventHandler>();
        services.TryAddScoped<FrontierEventHandler>();
        services.TryAddScoped<ReviewLoopEventHandler>();
        services.TryAddScoped<IntegrationEventHandler>();
        services.TryAddScoped<ParentReviewEventHandler>();
        services.TryAddScoped<TestingEventHandler>();
        services.TryAddScoped<CompletionEventHandler>();
    }
}
