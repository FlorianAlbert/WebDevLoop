using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Attention;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Web.Background;
using WebDevLoop.Web.Tests.Workflow;

namespace WebDevLoop.Web.Tests.Composition;

/// <summary>The real composition root (Program.cs) without any test replacement.</summary>
public sealed class CompositionRootTests
{
    /// <summary>
    /// Composed internally by <c>ExternalStateReconciler</c> and the attention resolution pipeline (several implementations by
    /// design), or markers implemented by exceptions rather than registered.
    /// </summary>
    private static readonly Type[] NotRegisteredByInterface =
        [typeof(ISpecReconciliationStep), typeof(IKnownRemediation), typeof(IAttentionStage), typeof(ITransientFault)];

    private static readonly Type[] Ports =
    [
        .. typeof(IClock).Assembly.GetExportedTypes().Where(type => type.IsInterface && !NotRegisteredByInterface.Contains(type)),
        typeof(IGitCredentialSource),
    ];

    [Fact]
    public async Task every_core_port_launcher_and_recovery_stage_has_exactly_one_registration_that_resolves()
    {
        using var sandbox = new WorkflowSandbox();
        await using var host = new CompositionHost(sandbox);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();

        string[] problems = [.. Ports.Select(port => ProblemOf(port, host.Registrations, scope.ServiceProvider)).OfType<string>()];

        Assert.True(Ports.Length > 40, $"Only {Ports.Length} ports were discovered.");
        Assert.Empty(problems);
    }

    [Fact]
    public async Task the_attention_resolution_pipeline_runs_known_remediation_first_and_every_remediation_resolves()
    {
        using var sandbox = new WorkflowSandbox();
        await using var host = new CompositionHost(sandbox);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();

        IAttentionStage[] stages = [.. scope.ServiceProvider.GetServices<IAttentionStage>()];
        AttentionCode[] remediated = [.. scope.ServiceProvider.GetServices<IKnownRemediation>().Select(remediation => remediation.Code).Order()];

        Assert.Collection(stages, stage => Assert.IsType<KnownRemediationStage>(stage), stage => Assert.IsType<TroubleshooterStage>(stage));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AttentionTriageService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AttentionTriageEventHandler>());
        AttentionCode[] expected =
        [
            AttentionCode.IntegrationBranchExists,
            AttentionCode.ExplorationFailed,
            AttentionCode.WorktreeNotClean,
            AttentionCode.TicketBranchNotBasedOnIntegration,
            AttentionCode.IntegrationTemporaryFailure,
            AttentionCode.TicketHasNoChanges,
        ];
        Assert.Equal(expected.Order(), remediated);
    }

    [Fact]
    public async Task the_process_boot_time_is_pinned_during_startup_before_any_work_can_start()
    {
        using var sandbox = new WorkflowSandbox();
        await using var host = new CompositionHost(sandbox);
        _ = host.Services;
        DateTimeOffset started = DateTimeOffset.UtcNow;
        await Task.Delay(50, TestContext.Current.CancellationToken);

        ProcessBoot boot = host.Services.GetRequiredService<ProcessBoot>();

        Assert.True(boot.StartedAt <= started, $"Boot time {boot.StartedAt:O} was taken after the host had started ({started:O}).");
    }

    [Fact]
    public async Task hosted_workers_start_with_the_log_flush_and_the_background_work_runner_first_so_they_stop_last()
    {
        using var sandbox = new WorkflowSandbox();
        await using var host = new CompositionHost(sandbox);

        Type[] workers = [.. host.Services.GetServices<IHostedService>().Select(service => service.GetType()).Where(type => type.Namespace == typeof(BackgroundWorkRunner).Namespace)];

        Assert.Equal(
            [typeof(AgentLogShutdownFlush), typeof(BackgroundWorkRunner), typeof(WorkflowEventSubscriptions), typeof(RecoveryWorker), typeof(OutboxDispatchWorker),
             typeof(MergeTrackingWorker), typeof(CopilotRuntimeMaintenanceWorker), typeof(OutboxRetentionWorker)],
            workers);
    }

    [Fact]
    public async Task disabling_the_workflow_serves_the_ui_and_api_without_hosted_workers()
    {
        using var sandbox = new WorkflowSandbox();
        await using var host = new CompositionHost(sandbox, ("WebDevLoop:Workflow:Enabled", "false"));

        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service => service.GetType().Namespace == typeof(BackgroundWorkRunner).Namespace);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.CreateClient().GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken)).StatusCode);
    }

    private static string? ProblemOf(Type port, IReadOnlyList<ServiceDescriptor> registrations, IServiceProvider services)
    {
        int count = registrations.Count(descriptor => descriptor.ServiceType == port);
        if (count != 1)
        {
            return $"{port.Name} is registered {count} times.";
        }

        try
        {
            services.GetRequiredService(port);
            return null;
        }
        catch (Exception exception)
        {
            return $"{port.Name} does not resolve: {exception.Message}";
        }
    }

    private sealed class CompositionHost(WorkflowSandbox sandbox, params (string Key, string Value)[] settings) : WebApplicationFactory<WebAssemblyMarker>
    {
        public IReadOnlyList<ServiceDescriptor> Registrations { get; private set; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("WebDevLoop:DataDirectory", sandbox.DataDirectory);
            foreach ((string key, string value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services => Registrations = [.. services]);
        }
    }
}
