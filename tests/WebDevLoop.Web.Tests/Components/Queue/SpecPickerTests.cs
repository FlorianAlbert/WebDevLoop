using Bunit;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;
using WebDevLoop.Web.Components.Queue;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.Tests.Components.Dashboard;

namespace WebDevLoop.Web.Tests.Components.Queue;

public sealed class SpecPickerTests : UiTestContext
{
    public SpecPickerTests()
    {
        Repositories.Repositories.Add(ApiData.Repository());
        Selection.Select(1);
        foreach (int number in new[] { 100, 200, 300 })
        {
            Issues.Seed(number, $"Spec {number}");
            Issues.Seed(number + 1, "Ticket", parent: number);
        }
    }

    [Theory]
    [InlineData(EnqueueOutcome.Queued, "Queued #100.")]
    [InlineData(EnqueueOutcome.AlreadyQueued, "#100 is already queued.")]
    [InlineData(EnqueueOutcome.RepositoryNotFound, "This repository no longer exists. Select a connected repository.")]
    [InlineData(EnqueueOutcome.ConcurrencyConflict, "The queue changed concurrently. Refresh the queue and retry.")]
    [InlineData(EnqueueOutcome.Ineligible, "Restore the webdevloop label and retry.")]
    [InlineData(EnqueueOutcome.GitHubUnavailable, "Check GitHub access and retry.")]
    [InlineData(EnqueueOutcome.Unavailable, "Restore workflow prerequisites on Health and retry.")]
    [InlineData(EnqueueOutcome.Failed, "Check the queue before retrying.")]
    public void typed_form_and_picker_show_the_same_actionable_outcome_message(EnqueueOutcome outcome, string expectedMessage)
    {
        Enqueuer.Result = new EnqueueResult(outcome,
            outcome is EnqueueOutcome.Queued or EnqueueOutcome.AlreadyQueued ? new RunId("run-100") : null,
            expectedMessage);
        IRenderedComponent<EnqueueSpecForm> typed = Render<EnqueueSpecForm>(parameters => parameters.Add(component => component.RepositoryId, 1));
        IRenderedComponent<SpecPicker> picker = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));

        typed.Find("input[name=issueNumber]").Input("100");
        typed.Find("form").Submit();
        picker.Find("input[aria-label='Select #100']").Change(true);
        picker.Find("[data-testid=queue-selection]").Click();

        Assert.Contains(expectedMessage, typed.Find("[data-testid=enqueue-result]").TextContent);
        Assert.Contains(expectedMessage, picker.Find("[data-testid=selection-result]").TextContent);
    }

    [Fact]
    public async Task a_slow_lookup_keeps_search_responsive_and_cannot_replace_a_newer_search()
    {
        var pending = new TaskCompletionSource<ParentIssuePage>();
        Issues.OnListParents = (_, _) => pending.Task;
        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));
        Assert.Contains("Loading current GitHub specs", cut.Markup);
        Assert.Equal("true", cut.Find("[aria-busy=true]").GetAttribute("aria-busy"));
        Assert.False(cut.Find("#spec-search").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid=queue-selection]").HasAttribute("disabled"));
        Issues.OnListParents = null;
        cut.Find("#spec-search").Change("#200");

        cut.Find("form").Submit();
        pending.SetResult(new ParentIssuePage([new(Issues.Ref(100), "Old result")], null));
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Single(cut.FindAll("input[aria-label^='Select #']"));
        Assert.NotEmpty(cut.FindAll("input[aria-label='Select #200']"));
    }

    [Fact]
    public async Task an_old_lookup_failure_does_not_replace_a_successful_new_search_with_an_error()
    {
        var pending = new TaskCompletionSource<ParentIssuePage>();
        Issues.OnListParents = (_, _) => pending.Task;
        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));
        Issues.OnListParents = null;
        cut.Find("#spec-search").Change("#200");
        cut.Find("form").Submit();
        int renders = cut.RenderCount;

        await cut.InvokeAsync(() => pending.SetException(new GitHubApiException(GitHubApiErrorKind.Transient, "Old request failed.")));
        cut.WaitForState(() => cut.RenderCount > renders);

        Assert.Empty(cut.FindAll("[role=alert]"));
        Assert.NotEmpty(cut.FindAll("input[aria-label='Select #200']"));
    }

    [Fact]
    public async Task search_refresh_and_parent_only_filter_keep_selection_and_observe_current_github()
    {
        Issues.Seed(900, "Open blocker");
        await Issues.AddBlockedByAsync(Issues.Ref(100), Issues.Ref(900), CancellationToken.None);
        await Issues.AddBlockedByAsync(Issues.Ref(201), Issues.Ref(900), CancellationToken.None);
        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));
        cut.Find("input[aria-label='Select #100']").Change(true);

        cut.Find("#spec-unblocked").Change(true);
        Assert.Empty(cut.FindAll("input[aria-label='Select #100']"));
        Assert.NotEmpty(cut.FindAll("input[aria-label='Select #200']"));
        Assert.NotEmpty(cut.FindAll("[data-selected-issue='100']"));
        cut.Find("#spec-search").Change("#200");
        cut.Find("form").Submit();
        Assert.Single(cut.FindAll("input[aria-label^='Select #']"));
        Issues.SetLabels(201);
        cut.Find("[data-testid=refresh-specs]").Click();

        Assert.Empty(cut.FindAll("input[aria-label^='Select #']"));
        Assert.Contains("No eligible specs match", cut.Markup);
        Assert.NotEmpty(cut.FindAll("[data-selected-issue='100']"));
    }

    [Fact]
    public void lookup_failure_clears_stale_candidates_and_refresh_can_recover()
    {
        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));
        Issues.IssueReadFailure = new GitHubApiException(GitHubApiErrorKind.Forbidden, "Permission denied.");

        cut.Find("[data-testid=refresh-specs]").Click();

        Assert.Contains("Check GitHub access and refresh", cut.Find("[role=alert]").TextContent);
        Assert.Contains("Permission denied", cut.Find("[role=alert]").TextContent);
        Assert.Empty(cut.FindAll("input[aria-label^='Select #']"));
        Issues.IssueReadFailure = null;
        cut.Find("[data-testid=refresh-specs]").Click();
        Assert.Empty(cut.FindAll("[role=alert]"));
        Assert.Equal(3, cut.FindAll("input[aria-label^='Select #']").Count);
    }

    [Fact]
    public void every_item_has_feedback_and_failed_items_stay_selected_for_retry()
    {
        Enqueuer.OnEnqueue = (number, _) => Task.FromResult(number switch
        {
            100 => new EnqueueResult(EnqueueOutcome.Ineligible, Reason: "Restore the webdevloop label and retry."),
            200 => new EnqueueResult(EnqueueOutcome.AlreadyQueued, new RunId("existing-run")),
            _ => new EnqueueResult(EnqueueOutcome.Queued, new RunId("new-run")),
        });
        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));
        foreach (int number in new[] { 100, 200, 300 })
        {
            cut.Find($"input[aria-label='Select #{number}']").Change(true);
        }

        cut.Find("[data-testid=queue-selection]").Click();

        Assert.Equal(3, cut.FindAll("[data-testid=selection-result]").Count);
        Assert.Contains("Restore the webdevloop label", cut.Find("[data-issue='100']").TextContent);
        Assert.Equal("/runs/existing-run", cut.Find("[data-issue='200'] a").GetAttribute("href"));
        Assert.Equal("/runs/new-run", cut.Find("[data-issue='300'] a").GetAttribute("href"));
        Assert.Equal("100", Assert.Single(cut.FindAll("[data-selected-issue]")).GetAttribute("data-selected-issue"));
        cut.Find("button[aria-label='Remove #100']").Click();
        Assert.True(cut.Find("[data-testid=queue-selection]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task submission_disables_selection_ordering_and_duplicate_submission_until_finished()
    {
        var pending = new TaskCompletionSource<EnqueueResult>();
        Enqueuer.OnEnqueue = (_, _) => pending.Task;
        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));
        cut.Find("input[aria-label='Select #100']").Change(true);

        Task submission = cut.Find("[data-testid=queue-selection]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("[data-testid=queue-selection]").GetAttribute("aria-busy")));
        Assert.All(cut.FindAll("input, button"), element => Assert.True(element.HasAttribute("disabled")));
        Assert.Contains("Queueing selection", cut.Markup);
        pending.SetResult(new(EnqueueOutcome.Queued, new RunId("queued")));
        await submission;

        Assert.Single(cut.FindAll("[data-testid=selection-result]"));
        Assert.Single(Enqueuer.Calls);
    }

    [Fact]
    public async Task diagnostic_only_mode_disables_picker_mutations_and_explains_how_to_recover()
    {
        await EnterDiagnosticModeAsync();
        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));

        Assert.All(cut.FindAll("input[aria-label^='Select #']"), element => Assert.True(element.HasAttribute("disabled")));
        Assert.True(cut.Find("[data-testid=queue-selection]").HasAttribute("disabled"));
        Assert.Contains("diagnostic-only", cut.Markup);
        Assert.NotEmpty(cut.FindAll("a[href='/health']"));
        Assert.Empty(Enqueuer.Calls);
    }

    [Fact]
    public async Task live_queue_changes_exclude_newly_queued_specs_without_refetching_github()
    {
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();
        Assert.NotEmpty(cut.FindAll("input[aria-label='Select #100']"));
        Runs.SpecRuns.Add(ApiData.SpecRun("existing") with { ParentIssueNumber = 100 });

        await EventBus.PublishAsync(SpecStatusChanged("existing", 1, SpecRunStatus.Queued), CancellationToken.None);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("input[aria-label='Select #100']")));
    }

    [Fact]
    public void loading_more_uses_the_applied_search_not_unsubmitted_input()
    {
        for (int number = 1; number <= 31; number++)
        {
            Issues.Seed(number, $"Spec {number}");
            Issues.Seed(1000 + number, "Ticket", parent: number);
        }

        IRenderedComponent<SpecPicker> cut = Render<SpecPicker>(parameters => parameters.Add(component => component.RepositoryId, 1));
        cut.Find("#spec-search").Change("Spec 2");
        cut.Find("form").Submit();
        Assert.NotEmpty(cut.FindAll("input[aria-label='Select #2']"));
        cut.Find("#spec-search").Change("Unsubmitted search");
        cut.Find("[data-testid=more-specs]").Click();

        Assert.NotEmpty(cut.FindAll("input[aria-label='Select #200']"));
    }

    [Fact]
    public void selects_multiple_specs_orders_them_with_named_native_buttons_and_submits_in_order()
    {
        IRenderedComponent<QueuePage> cut = Render<QueuePage>();
        cut.Find("input[aria-label='Select #100']").Change(true);
        cut.Find("input[aria-label='Select #200']").Change(true);
        cut.Find("input[aria-label='Select #300']").Change(true);
        cut.Find("button[aria-label='Move #300 up']").Click();

        Assert.Equal(["100", "300", "200"], cut.FindAll("[data-selected-issue]").Select(item => item.GetAttribute("data-selected-issue")));
        Assert.True(cut.Find("button[aria-label='Move #100 up']").HasAttribute("disabled"));
        Assert.True(cut.Find("button[aria-label='Move #200 down']").HasAttribute("disabled"));
        cut.Find("[data-testid=queue-selection]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal([(1, 100), (1, 300), (1, 200)], Enqueuer.Calls);
            Assert.Equal(3, cut.FindAll("[data-testid=selection-result]").Count);
            Assert.Empty(cut.FindAll("[data-selected-issue]"));
        });
        Assert.NotEmpty(cut.FindAll("input[name=issueNumber]"));
    }
}
