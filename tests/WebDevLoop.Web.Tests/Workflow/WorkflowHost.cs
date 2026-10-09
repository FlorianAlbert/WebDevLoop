using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>
/// The real app (Program.cs, every hosted worker, SQLite and the real git workspace against the sandbox's bare remote) with
/// only GitHub, Copilot, the tester's app host and the prerequisite probes faked. Timers are shortened so a workflow
/// finishes within seconds. Several hosts may run one after another on the same sandbox (restart recovery).
/// </summary>
internal sealed class WorkflowHost(WorkflowScenario scenario, AgentScript? script = null) : WebApplicationFactory<WebAssemblyMarker>
{
    public static readonly IReadOnlyDictionary<string, string?> FastTimers = new Dictionary<string, string?>
    {
        ["WebDevLoop:Workflow:OutboxPollInterval"] = "00:00:00.050",
        ["WebDevLoop:Workflow:StartupRetryInterval"] = "00:00:00.100",
        ["WebDevLoop:Workflow:RecoveryInterval"] = "00:00:00.500",
        ["WebDevLoop:Workflow:MergeTrackingInterval"] = "00:00:00.200",
        ["WebDevLoop:Workflow:RuntimeMaintenanceInterval"] = "00:00:00.200",
        ["WebDevLoop:Workflow:ParkedIntegrationRetryInterval"] = "00:00:00.500",
    };

    public AgentScript Script { get; } = script ?? new AgentScript();

    public ScriptedPrerequisites Prerequisites { get; } = new();

    public IdleCopilotRuntimePool Runtimes { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("WebDevLoop:DataDirectory", scenario.Sandbox.DataDirectory);
        foreach ((string key, string? value) in FastTimers)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(scenario.Logs));
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IGitHubIssues>(scenario.Issues));
            services.Replace(ServiceDescriptor.Singleton<IGitHubPullsAndStacks>(scenario.Pulls));
            services.Replace(ServiceDescriptor.Singleton<IPrerequisiteValidator>(Prerequisites));
            services.Replace(ServiceDescriptor.Singleton<ICopilotRuntimePool>(Runtimes));
            services.Replace(ServiceDescriptor.Singleton<ITestTargetRunner>(new InstantTestTargets()));
            services.Replace(ServiceDescriptor.Singleton<IAgentRunner>(provider => new ScriptedAgentRunner(
                provider.GetRequiredService<IServiceScopeFactory>(), scenario.Issues, Script, scenario.Journal)));
        });
    }
}

/// <summary>The external world of one scenario (git remote, GitHub, agent journal); it outlives app restarts.</summary>
internal sealed class WorkflowScenario : IDisposable
{
    public WorkflowScenario(params int[] ticketIssues)
    {
        Issues = new FakeGitHubIssues(new Core.Domain.GitHubRepoRef("acme", "widgets"));
        Pulls = new FakeGitHubPulls(Sandbox);
        Journal = new AgentJournal { KnownTickets = ticketIssues };
    }

    public WorkflowSandbox Sandbox { get; } = new();

    public FakeGitHubIssues Issues { get; }

    public FakeGitHubPulls Pulls { get; }

    public AgentJournal Journal { get; }

    public CapturedLogs Logs { get; } = new();

    public void Dispose() => Sandbox.Dispose();
}
