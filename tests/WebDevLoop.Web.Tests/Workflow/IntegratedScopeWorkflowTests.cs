using System.Collections.Concurrent;
using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class IntegratedScopeWorkflowTests
{
    [Fact]
    public async Task Partial_spec_reviews_and_tests_integrated_work_without_recreating_excluded_requirements_but_still_fixes_genuine_findings()
    {
        using var scenario = new WorkflowScenario(101, 102);
        scenario.Issues.Seed(100, "Create and export items");
        scenario.Issues.SetBody(100, "Users can create items with a non-empty title. Users can export items.");
        scenario.Issues.Seed(101, "Create items", parent: 100);
        scenario.Issues.SetBody(101, "Users can create items with a non-empty title.");
        scenario.Issues.Seed(102, "Export items", parent: 100);
        scenario.Issues.SetLabels(102);
        var reviews = new ConcurrentQueue<string>();
        var tests = new ConcurrentQueue<string>();
        int specificationReviews = 0;
        var script = new AgentScript
        {
            ParentReview = request =>
            {
                reviews.Enqueue(request.Prompt);
                FindingAxis axis = request.Role == AgentRole.ReviewerSpecification ? FindingAxis.Specification : FindingAxis.CodingStandards;
                return axis == FindingAxis.Specification && Interlocked.Increment(ref specificationReviews) == 1
                    ? ReviewReport.IssuesFound(axis, "Export is excluded; creating items accepts an empty title.", [new SpecificationFinding(
                        SpecificationFindingKind.Incorrect, "Users can create items with a non-empty title.", "ticket-101.txt", 1,
                        "Creating an item accepts an empty title.", "Reject empty titles.", title: "Reject empty item titles")])
                    : ReviewReport.Clean(axis, "Integrated work is correct; export remains intentionally excluded.");
            },
            Test = request =>
            {
                tests.Enqueue(request.Prompt);
                return tests.Count == 1
                    ? new TestReport(TestVerdict.IssuesFound, "Export is excluded; creating duplicate items crashes.", [], [new TestIssue(
                        "Creating duplicate items crashes", TestIssueSeverity.Major, "Users can create items.",
                        ["Create an item", "Create another item with the same title"], "Both items are created.",
                        "The application crashes.", ["duplicate-item.log"])])
                    : new TestReport(TestVerdict.Pass, "Integrated workflows pass; export remains intentionally excluded.",
                        [new TestScenario("Create items", TestScenarioOutcome.Passed)], []);
            },
        };
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);

        int repositoryId = await api.RegisterRepositoryAsync(scenario, Path.Combine(scenario.Sandbox.DataDirectory, "workspaces", "repo"));
        string runId = await api.EnqueueAsync(repositoryId, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        scenario.Issues.SetLabels(101);
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForStatusAsync(runId, nameof(SpecRunStatus.AwaitingMerge));

        JsonElement[] tickets = await api.TicketsAsync(runId);
        Assert.Equal([101, 102, 103, 104], tickets.Select(ticket => ticket.GetProperty("issueNumber").GetInt32()).Order());
        Assert.All(tickets.Where(ticket => ticket.GetProperty("issueNumber").GetInt32() != 102),
            ticket => Assert.Equal(nameof(TicketRunStatus.Integrated), ticket.GetProperty("status").GetString()));
        Assert.Equal(nameof(TicketRunStatus.Blocked), tickets.Single(ticket => ticket.GetProperty("issueNumber").GetInt32() == 102).GetProperty("status").GetString());
        Assert.Equal("Reject empty item titles", (await scenario.Issues.GetIssueAsync(scenario.Issues.Ref(103), TestContext.Current.CancellationToken)).Title);
        Assert.Equal("Creating duplicate items crashes", (await scenario.Issues.GetIssueAsync(scenario.Issues.Ref(104), TestContext.Current.CancellationToken)).Title);
        Assert.Equal(6, reviews.Count);
        Assert.Equal(2, tests.Count);
        Assert.All(reviews.Concat(tests), prompt =>
        {
            string scope = prompt.Split("<integrated_tickets>\n", StringSplitOptions.None)[1];
            Assert.Contains("### #101: Create items\n\nUsers can create items with a non-empty title.", scope);
            Assert.DoesNotContain("#102", scope);
            Assert.DoesNotContain("Export items", scope);
            Assert.Contains("Do not fail solely because excluded work is absent", prompt);
            Assert.Contains("do not recreate excluded requirements as finding issues", prompt);
        });
        Assert.Contains("### #103: Reject empty item titles", tests.First());
        Assert.DoesNotContain("### #104:", tests.First());
        Assert.Contains("### #104: Creating duplicate items crashes", tests.Last());

        JsonElement[] stack = await api.StackAsync(runId);
        Assert.Equal(3, stack.Length);
        scenario.Sandbox.FastForwardTrunk(new CommitSha(stack[^1].GetProperty("commitSha").GetString()!));
        scenario.Pulls.Merge([.. stack.Select(layer => new PullRequestNumber(layer.GetProperty("pullRequestNumber").GetInt32()))]);
        await api.WaitForStatusAsync(runId, nameof(SpecRunStatus.Completed));

        Assert.DoesNotContain("ticket-102.txt", scenario.Sandbox.FilesOnTrunk());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.TicketIssue == 102);
        Assert.Equal(IssueState.Open, scenario.Issues.StateOf(102));
        Assert.Equal(4, (await scenario.Issues.GetSubIssuesAsync(scenario.Issues.Ref(100), TestContext.Current.CancellationToken)).Count);
    }
}
