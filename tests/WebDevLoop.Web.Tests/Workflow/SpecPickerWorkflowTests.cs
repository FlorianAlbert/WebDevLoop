using Bunit;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.GitHub.Issues;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Components.Queue;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class SpecPickerWorkflowTests
{
    [Fact]
    public async Task discovery_returns_a_cursor_and_following_it_reaches_the_remaining_eligible_specs()
    {
        using var scenario = new WorkflowScenario(1001);
        for (int number = 1; number <= 31; number++)
        {
            scenario.Issues.Seed(number, $"Spec {number}");
            scenario.Issues.Seed(1000 + number, "Ticket", parent: number);
        }

        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string path = $"/api/repos/{repositoryId}/spec-candidates";

        JsonElement first = await api.GetAsync(path);
        JsonElement second = await api.GetAsync(path + $"?page={first.GetProperty("nextPage").GetInt32()}");

        Assert.Equal(30, Numbers(first).Length);
        Assert.Equal([31], Numbers(second));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextPage").ValueKind);
    }

    [Fact]
    public async Task picker_submission_uses_real_enqueue_checks_for_stale_labels_closed_children_duplicates_and_github_errors()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(900, "Open parent blocker");
        foreach (int number in new[] { 100, 200, 300, 400, 500 })
        {
            scenario.Issues.Seed(number, $"Spec {number}", blockedBy: 900);
            scenario.Issues.Seed(number + 1, "Ticket", parent: number);
        }

        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        await using AsyncServiceScope appScope = host.Services.CreateAsyncScope();
        await using var ui = new BunitContext();
        ui.Services.AddSingleton(appScope.ServiceProvider.GetRequiredService<ISpecCandidateQueries>());
        ui.Services.AddSingleton(appScope.ServiceProvider.GetRequiredService<ISpecSelectionEnqueuer>());
        ui.Services.AddSingleton(host.Services.GetRequiredService<DiagnosticReadiness>());
        IRenderedComponent<SpecPicker> picker = ui.Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, repositoryId));
        picker.WaitForElement("input[aria-label='Select #500']", TimeSpan.FromSeconds(10));
        foreach (int number in new[] { 100, 200, 300, 400, 500 })
        {
            picker.Find($"input[aria-label='Select #{number}']").Change(true);
        }

        string existing = await api.EnqueueAsync(repositoryId, 100);
        scenario.Issues.SetLabels(200);
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(301), IssueCloseReason.Completed, Xunit.TestContext.Current.CancellationToken);
        scenario.Issues.IssueFailures[400] = new GitHubApiException(GitHubApiErrorKind.Transient, "GitHub unavailable; retry.");

        await picker.Find("[data-testid=queue-selection]").ClickAsync(new());

        Assert.Equal(5, picker.FindAll("[data-testid=selection-result]").Count);
        Assert.Contains("AlreadyQueued", picker.Find("[data-issue='100']").TextContent);
        Assert.Equal($"/runs/{existing}", picker.Find("[data-issue='100'] a").GetAttribute("href"));
        Assert.Contains("webdevloop", picker.Find("[data-issue='200']").TextContent);
        Assert.Contains("sub-issue", picker.Find("[data-issue='300']").TextContent);
        Assert.Contains("GitHubUnavailable", picker.Find("[data-issue='400']").TextContent);
        Assert.Contains("Queued", picker.Find("[data-issue='500']").TextContent);
        JsonElement queue = await api.GetAsync($"/api/repos/{repositoryId}/spec-runs");
        Assert.Equal([100, 500], queue.EnumerateArray().Select(run => run.GetProperty("parentIssueNumber").GetInt32()));
    }

    [Fact]
    public async Task ordered_batch_rechecks_stale_selection_reports_every_result_and_persists_successes_in_chosen_order()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(900, "Open parent blocker");
        foreach (int number in new[] { 100, 200, 300, 400, 500 })
        {
            scenario.Issues.Seed(number, $"Spec {number}", blockedBy: 900);
            scenario.Issues.Seed(number + 1, "Ticket", parent: number);
        }

        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        Assert.Equal(5, Numbers(await api.GetAsync($"/api/repos/{repositoryId}/spec-candidates")).Length);
        string existing = await api.EnqueueAsync(repositoryId, 300);
        scenario.Issues.SetLabels(200);
        scenario.Issues.IssueFailures[400] = new GitHubApiException(GitHubApiErrorKind.Forbidden, "Permission denied; restore GitHub access.");

        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/spec-runs/batch",
            new { specIssueNumbers = new[] { 500, 200, 400, 300, 100 } }, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement results = await response.Content.ReadFromJsonAsync<JsonElement>(Xunit.TestContext.Current.CancellationToken);
        Assert.Equal([500, 200, 400, 300, 100], results.EnumerateArray().Select(item => item.GetProperty("specIssueNumber").GetInt32()));
        Assert.Equal(["Queued", "Ineligible", "GitHubUnavailable", "AlreadyQueued", "Queued"],
            results.EnumerateArray().Select(item => item.GetProperty("outcome").GetString()));
        Assert.Contains("webdevloop", results[1].GetProperty("message").GetString());
        Assert.Contains("restore GitHub access", results[2].GetProperty("message").GetString());
        Assert.Equal(existing, results[3].GetProperty("runId").GetString());
        JsonElement queue = await api.GetAsync($"/api/repos/{repositoryId}/spec-runs");
        Assert.Equal([300, 500, 100], queue.EnumerateArray().Select(run => run.GetProperty("parentIssueNumber").GetInt32()));
    }

    [Fact]
    public async Task discovery_searches_across_pages_filters_only_parent_blockers_and_refreshes_current_opt_in()
    {
        using var scenario = new WorkflowScenario(101);
        for (int number = 1; number <= 31; number++)
        {
            scenario.Issues.Seed(number, "No eligible children");
        }

        scenario.Issues.Seed(100, "Searchable spec", blockedBy: 900);
        scenario.Issues.Seed(101, "Ticket", parent: 100, blockedBy: 900);
        scenario.Issues.Seed(200, "Searchable independent spec", blockedBy: 901);
        scenario.Issues.Seed(201, "Blocked ticket", parent: 200, blockedBy: 900);
        scenario.Issues.Seed(900, "Open blocker");
        scenario.Issues.Seed(901, "Closed blocker");
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(901), IssueCloseReason.Completed, Xunit.TestContext.Current.CancellationToken);
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string path = $"/api/repos/{repositoryId}/spec-candidates";

        JsonElement all = await api.GetAsync(path + "?search=searchable");
        Assert.Equal([100, 200], Numbers(all));
        JsonElement unblocked = await api.GetAsync(path + "?search=Searchable&onlyUnblocked=true");
        Assert.Equal([200], Numbers(unblocked));
        JsonElement numbered = await api.GetAsync(path + "?search=%23100");
        Assert.Equal([100], Numbers(numbered));

        scenario.Issues.SetLabels(201);
        Assert.Empty(Numbers(await api.GetAsync(path + "?onlyUnblocked=true")));
        scenario.Issues.SetLabels(201, WebDevLoopOptIn.Label);
        await api.EnqueueAsync(repositoryId, 100);
        JsonElement refreshed = await api.GetAsync(path);
        Assert.Equal([200], Numbers(refreshed));
    }

    private static int[] Numbers(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("issueNumber").GetInt32()).ToArray();
}
