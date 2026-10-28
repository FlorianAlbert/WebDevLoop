using Bunit;
using WebDevLoop.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Components.Repositories;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.DependencyInjection;
using WebDevLoop.Web.Tests.Workflow;

namespace WebDevLoop.Web.Tests.Components.Dashboard;

/// <summary>bUnit context with every application service replaced by a fake. Operational unless <see cref="EnterDiagnosticModeAsync"/> is called.</summary>
public abstract class UiTestContext : BunitContext
{
    internal FakeRepositoryRegistry Registry { get; } = new();

    internal FakeRepositoryQueries Repositories { get; } = new();

    internal FakeRepositoryLabels Labels { get; } = new();

    internal FakeRunQueries Runs { get; } = new();

    internal FakeSettingsManager Settings { get; } = new();

    internal FakeSpecEnqueuer Enqueuer { get; } = new();

    internal CurrentRepositorySelection Selection { get; } = new();

    internal CountingEventBus EventBus { get; } = new();

    internal FakePrerequisiteValidator Validator { get; } = new();

    internal DiagnosticReadiness Readiness { get; }

    internal FakeGitHubIssues Issues { get; } = new(new("acme", "widgets"));

    protected UiTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Readiness = new DiagnosticReadiness(Validator, new FakeClock());
        Readiness.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
        Services.AddSingleton<IRepositoryRegistry>(Registry);
        Services.AddSingleton<IRepositoryQueries>(Repositories);
        Services.AddSingleton<IGitHubRepositoryLabels>(Labels);
        Services.AddScoped<RepositoryOptIn>();
        Services.AddSingleton<IRunQueries>(Runs);
        Services.AddSingleton<IParentBlockerQueries>(Runs);
        Services.AddSingleton<ISettingsManager>(Settings);
        Services.AddSingleton<ISpecEnqueuer>(Enqueuer);
        Services.AddSingleton<IGitHubIssues>(Issues);
        Services.AddWebDevLoopSpecSelection();
        Services.AddSingleton<ICurrentRepositorySelection>(Selection);
        Services.AddSingleton<IRunEventBus>(EventBus);
        Services.AddSingleton(Readiness);
        Services.AddSingleton<RepositoryContext>();
    }

    internal async Task EnterDiagnosticModeAsync()
    {
        Validator.Checks = [new PrerequisiteCheck("GitHub auth", PrerequisiteStatus.Failed, "Nobody is signed in to GitHub.", "Sign in with GitHub on the GitHub page.")];
        await Readiness.RefreshAsync(CancellationToken.None);
    }

    internal static EffectiveSettingsView Effective(int maxActiveSpecs = 2) =>
        new("/ws", "/copilot", "main", maxActiveSpecs, 4, 2, 3, 2, 3, 3, "run it", new PortRangeData(5000, 5100), new Dictionary<AgentRole, EffectiveRoleSettingsView>(), true, 2);

    internal static EventEnvelope SpecStatusChanged(string specRunId, int repositoryId, SpecRunStatus to, long messageId = 1) =>
        new(messageId, new SpecRunStatusChanged(new RunId(specRunId), repositoryId, SpecRunStatus.Queued, to, ApiData.Now));
}
