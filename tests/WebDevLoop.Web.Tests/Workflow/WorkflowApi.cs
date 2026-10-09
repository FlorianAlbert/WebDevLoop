using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>Drives a workflow through the REST API, like a user of the app would.</summary>
internal sealed class WorkflowApi(HttpClient client, CapturedLogs? logs = null)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<int> RegisterRepositoryAsync(WorkflowScenario scenario)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/repos",
            new { owner = scenario.Issues.Repository.Owner, name = scenario.Issues.Repository.Name, cloneUrl = scenario.Sandbox.RemotePath },
            Ct);
        await EnsureAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt32();
    }

    public async Task UseDependencyModeAsync(int repositoryId, string mode)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/repos/{repositoryId}/settings", new { specDependencyMode = mode }, Ct);
        await EnsureAsync(response, HttpStatusCode.OK);
    }

    public async Task<string> EnqueueAsync(int repositoryId, int specIssue)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = specIssue }, Ct);
        await EnsureAsync(response, HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("runId").GetString()!;
    }

    public Task<JsonElement> GetAsync(string url) => client.GetFromJsonAsync<JsonElement>(url, Ct);

    public async Task<JsonElement[]> StackAsync(string runId) => [.. (await GetAsync($"/api/spec-runs/{runId}/stack")).EnumerateArray()];

    public async Task<JsonElement[]> TicketsAsync(string runId) => [.. (await GetAsync($"/api/spec-runs/{runId}/tickets")).EnumerateArray()];

    /// <summary>Polls the run until it reaches <paramref name="status"/>; fails fast with the run's state when it needs attention.</summary>
    public async Task<JsonElement> WaitForStatusAsync(string runId, string status, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        JsonElement run = default;
        try
        {
            while (true)
            {
                run = await GetAsync($"/api/spec-runs/{runId}");
                string current = run.GetProperty("status").GetString()!;
                if (current == status)
                {
                    return run;
                }

                if (current is "NeedsAttention" or "Aborted")
                {
                    break;
                }

                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
        }

        Assert.Fail($"Spec run {runId} did not reach {status}: {run}\nTickets: {string.Join("\n", await TicketsAsync(runId))}\nApp warnings and errors:\n{string.Join("\n", logs?.Entries ?? [])}");
        return run;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            Assert.Fail($"Expected {expected} but got {response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }
    }
}
