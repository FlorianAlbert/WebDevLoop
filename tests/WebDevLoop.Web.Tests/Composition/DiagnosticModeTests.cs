using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Tests.Workflow;

namespace WebDevLoop.Web.Tests.Composition;

public sealed class DiagnosticModeTests
{
    private static readonly PrerequisiteCheck MissingGit = new("Git CLI", PrerequisiteStatus.Failed, "git was not found.", "Install git.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task failed_prerequisites_serve_health_ui_and_api_but_never_start_the_workflow_until_a_recheck_passes()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        host.Prerequisites.Checks = [MissingGit];
        HttpClient client = host.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/health", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", Ct)).StatusCode);
        Assert.Contains("Git CLI", await client.GetStringAsync("/health", Ct));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/repos", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/repos/1/spec-runs", new { specIssueNumber = 1 }, Ct)).StatusCode);

        SchedulerStartGate schedulers = host.Services.GetRequiredService<SchedulerStartGate>();
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        Assert.False(schedulers.IsOpen);
        Assert.Equal(0, host.Runtimes.MaintenancePasses);

        host.Prerequisites.Checks = [ScriptedPrerequisites.Passed];
        await host.Services.GetRequiredService<DiagnosticReadiness>().RefreshAsync(Ct);

        await schedulers.WaitUntilOpenAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", Ct)).StatusCode);
        await WaitUntilAsync(() => host.Runtimes.MaintenancePasses > 0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, deadline.Token);
        }
    }
}
