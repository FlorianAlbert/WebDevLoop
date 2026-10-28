using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Components.Queue;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class TypedEnqueueWorkflowTests
{
    [Theory]
    [InlineData("#100", "parent", "webdevloop")]
    [InlineData("https://github.com/acme/widgets/issues/100", "child", "sub-issue")]
    [InlineData("100", "closed", "open")]
    [InlineData("https://github.com/acme/widgets/issues/100", "github", "permission denied")]
    public async Task typed_submission_uses_current_shared_eligibility_and_displays_its_reason(string input, string change, string reason)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        await using AsyncServiceScope appScope = host.Services.CreateAsyncScope();
        await using var ui = new BunitContext();
        ui.Services.AddSingleton(appScope.ServiceProvider.GetRequiredService<ISpecEnqueuer>());
        ui.Services.AddSingleton(host.Services.GetRequiredService<DiagnosticReadiness>());
        IRenderedComponent<EnqueueSpecForm> form = ui.Render<EnqueueSpecForm>(parameters => parameters.Add(component => component.RepositoryId, repositoryId));
        form.Find("input").Input(input);

        switch (change)
        {
            case "parent":
                scenario.Issues.SetLabels(100);
                break;
            case "child":
                scenario.Issues.SetLabels(101);
                break;
            case "closed":
                await scenario.Issues.CloseAsync(scenario.Issues.Ref(100), IssueCloseReason.Completed, Xunit.TestContext.Current.CancellationToken);
                break;
            case "github":
                scenario.Issues.SubIssueReadFailure = new GitHubApiException(GitHubApiErrorKind.Forbidden, "GitHub permission denied.");
                break;
        }

        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Contains(reason, form.Find("[data-testid=enqueue-result]").TextContent), TimeSpan.FromSeconds(10));
        Assert.Empty(form.FindAll("[data-testid=enqueue-result] a"));
        Assert.Empty((await api.GetAsync($"/api/repos/{repositoryId}/spec-runs")).EnumerateArray());
    }
}
