using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Queue;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.Tests.Components.Dashboard;

namespace WebDevLoop.Web.Tests.Components.Queue;

public sealed class QueuePageTests : UiTestContext
{
    public QueuePageTests()
    {
        Repositories.Repositories.Add(ApiData.Repository(1, "widgets"));
        Repositories.Repositories.Add(ApiData.Repository(2, "gadgets"));
        Settings.EffectiveResult = CommandResult<EffectiveSettingsView>.Succeeded(Effective(maxActiveSpecs: 2));
        Selection.Select(1);
    }

    private static SpecRunView Run(string id, SpecRunStatus status, int position, int repositoryId = 1, int issue = 10, SpecDependencyMode? mode = null, string? failure = null) =>
        ApiData.SpecRun(id, repositoryId, status, position) with { ParentIssueNumber = issue, Title = $"Spec {issue}", DependencyModeUsed = mode, FailureReason = failure };

    [Fact]
    public void asks_to_pick_a_repository_when_none_is_selected()
    {
        Selection.Select(null);

        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        Assert.Contains("Select a repository", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void groups_the_queue_by_lane_and_shows_active_slot_usage()
    {
        Runs.SpecRuns.AddRange([
            Run("r-active", SpecRunStatus.Running, 1, issue: 11),
            Run("r-merge", SpecRunStatus.AwaitingMerge, 2, issue: 12),
            Run("r-attn", SpecRunStatus.NeedsAttention, 3, issue: 13, failure: "Tests keep failing"),
            Run("r-wait", SpecRunStatus.Queued, 4, issue: 14),
            Run("r-done", SpecRunStatus.Completed, 5, issue: 15),
            Run("r-other-repo", SpecRunStatus.Running, 1, repositoryId: 2, issue: 99)]);

        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        Assert.Contains("acme/widgets", cut.Find("h1").TextContent);
        Assert.Contains("1 / 2", cut.Find("[data-testid=active-slots]").TextContent);
        Assert.Equal(["r-active"], LaneRunIds(cut, SpecRunLane.Active));
        Assert.Equal(["r-merge"], LaneRunIds(cut, SpecRunLane.AwaitingMerge));
        Assert.Equal(["r-attn"], LaneRunIds(cut, SpecRunLane.NeedsAttention));
        Assert.Equal(["r-wait"], LaneRunIds(cut, SpecRunLane.Waiting));
        Assert.Equal(["r-done"], LaneRunIds(cut, SpecRunLane.Completed));
        Assert.Contains("Tests keep failing", cut.Find("[data-run-id=r-attn]").TextContent);
        Assert.Equal("/runs/r-active", cut.Find("[data-run-id=r-active] a").GetAttribute("href"));
    }

    [Fact]
    public void dependent_spec_waits_under_wait_for_merge()
    {
        Runs.SpecRuns.Add(Run("r-dep", SpecRunStatus.WaitingForDependency, 2, mode: SpecDependencyMode.WaitForMerge));

        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        var row = cut.Find("[data-lane=Waiting] [data-run-id=r-dep]");
        Assert.Contains("Waiting for dependency", row.TextContent);
        Assert.Contains("Mode: Wait for merge", row.TextContent);
        Assert.Contains("merged", row.TextContent);
        Assert.Equal("Mode: Wait for merge", cut.Find("[data-testid=dependency-mode]").TextContent.Trim());
    }

    [Fact]
    public void waiting_note_names_the_blocking_specs_that_are_not_merged_yet()
    {
        Runs.SpecRuns.Add(Run("r-dep", SpecRunStatus.WaitingForDependency, 3, mode: SpecDependencyMode.WaitForMerge));
        Runs.Dependencies["r-dep"] =
        [
            new SpecDependencyView("r-a", "acme/widgets#12", 12, "Foundations", SpecRunStatus.Running, false),
            new SpecDependencyView(null, "acme/widgets#5", 5, null, null, false),
            new SpecDependencyView("r-b", "acme/widgets#9", 9, "Done", SpecRunStatus.Completed, true),
        ];

        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        string note = cut.Find("[data-testid=run-note-r-dep]").TextContent;
        Assert.Contains("#12 (Running)", note);
        Assert.Contains("#5 (not tracked)", note);
        Assert.DoesNotContain("#9", note);
    }

    [Fact]
    public void waiting_run_without_recorded_mode_falls_back_to_the_effective_dependency_mode()
    {
        Settings.EffectiveResult = CommandResult<EffectiveSettingsView>.Succeeded(Effective(mode: SpecDependencyMode.StackOnTop));
        Runs.SpecRuns.Add(Run("r-dep", SpecRunStatus.WaitingForDependency, 2));

        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        Assert.Contains("Stack on top", cut.Find("[data-run-id=r-dep]").TextContent);
    }

    [Fact]
    public void enqueues_the_entered_issue_number_for_the_current_repository()
    {
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        cut.Find("input[name=issueNumber]").Input("42");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("run-42", cut.Find("[data-testid=enqueue-result]").InnerHtml));
        Assert.Equal([(1, 42)], Enqueuer.Calls);
        Assert.Equal("/runs/run-42", cut.Find("[data-testid=enqueue-result] a").GetAttribute("href"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public void rejects_an_invalid_issue_number_without_calling_the_enqueuer(string input)
    {
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        cut.Find("input[name=issueNumber]").Input(input);
        cut.Find("form").Submit();

        Assert.Contains("positive issue number", cut.Find("[data-testid=enqueue-validation]").TextContent);
        Assert.Equal("true", cut.Find("input[name=issueNumber]").GetAttribute("aria-invalid"));
        Assert.Empty(Enqueuer.Calls);
    }

    [Theory]
    [InlineData("#123", 123)]
    [InlineData(" 7 ", 7)]
    [InlineData("https://github.com/acme/widgets/issues/55", 55)]
    [InlineData("https://github.com/acme/widgets/issues/55#issuecomment-1", 55)]
    public void accepts_hash_prefixed_numbers_and_issue_urls(string input, int expected)
    {
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        Assert.Equal("numeric", cut.Find("input[name=issueNumber]").GetAttribute("inputmode"));
        cut.Find("input[name=issueNumber]").Input(input);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal([(1, expected)], Enqueuer.Calls));
        Assert.Empty(cut.FindAll("[data-testid=enqueue-validation]"));
    }

    [Fact]
    public void has_exactly_one_h1_without_a_selected_repository()
    {
        Selection.Select(null);

        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        Assert.Equal("Queue", cut.Find("h1").TextContent.Trim());
        Assert.Single(cut.FindAll("h1"));
    }

    [Theory]
    [InlineData(EnqueueOutcome.AlreadyQueued, "already queued")]
    [InlineData(EnqueueOutcome.RepositoryNotFound, "no longer exists")]
    [InlineData(EnqueueOutcome.ConcurrencyConflict, "try again")]
    public void explains_every_non_queued_outcome(EnqueueOutcome outcome, string expectedText)
    {
        Enqueuer.Result = new EnqueueResult(outcome, outcome == EnqueueOutcome.AlreadyQueued ? new RunId("run-7") : null);
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        cut.Find("input[name=issueNumber]").Input("7");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(expectedText, cut.Find("[data-testid=enqueue-result]").TextContent, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task disables_queueing_in_diagnostic_only_mode()
    {
        await EnterDiagnosticModeAsync();

        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        Assert.True(cut.Find("input[name=issueNumber]").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid=enqueue-submit]").HasAttribute("disabled"));
        Assert.Contains("diagnostic-only", cut.Find("[data-testid=enqueue-disabled]").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task live_status_event_updates_the_queue_without_a_page_reload()
    {
        Runs.SpecRuns.Add(Run("r-1", SpecRunStatus.Queued, 1));
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();
        Assert.Equal(["r-1"], LaneRunIds(cut, SpecRunLane.Waiting));

        Runs.SpecRuns[0] = Runs.SpecRuns[0] with { Status = SpecRunStatus.Running };
        await EventBus.PublishAsync(SpecStatusChanged("r-1", 1, SpecRunStatus.Running), CancellationToken.None);

        cut.WaitForAssertion(() => Assert.Equal(["r-1"], LaneRunIds(cut, SpecRunLane.Active)));
        Assert.Empty(LaneRunIds(cut, SpecRunLane.Waiting));
    }

    [Fact]
    public void switching_the_current_repository_shows_that_repositorys_queue()
    {
        Runs.SpecRuns.Add(Run("r-widgets", SpecRunStatus.Running, 1));
        Runs.SpecRuns.Add(Run("r-gadgets", SpecRunStatus.Running, 1, repositoryId: 2));
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();

        Services.GetRequiredService<WebDevLoop.Web.Components.Repositories.RepositoryContext>().Select(2);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("acme/gadgets", cut.Find("h1").TextContent);
            Assert.Equal(["r-gadgets"], LaneRunIds(cut, SpecRunLane.Active));
        });
    }

    [Fact]
    public async Task unsubscribes_from_the_event_bus_when_disposed()
    {
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();
        Assert.Equal(1, EventBus.SubscriberCount);

        await DisposeAsync();

        Assert.Equal(0, EventBus.SubscriberCount);
    }

    private static string[] LaneRunIds(IRenderedComponent<QueuePage> cut, SpecRunLane lane) =>
        cut.FindAll($"[data-lane={lane}] [data-run-id]").Select(row => row.GetAttribute("data-run-id")!).ToArray();
}
