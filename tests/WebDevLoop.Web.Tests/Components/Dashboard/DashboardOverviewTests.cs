using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Dashboard;
using WebDevLoop.Web.Components.Repositories;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Dashboard;

public sealed class DashboardOverviewTests : UiTestContext
{
    public DashboardOverviewTests()
    {
        Repositories.Repositories.Add(ApiData.Repository(1, "widgets"));
        Repositories.Repositories.Add(ApiData.Repository(2, "gadgets"));
        Settings.EffectiveResult = CommandResult<EffectiveSettingsView>.Succeeded(Effective(maxActiveSpecs: 2));
    }

    private static SpecRunView Run(string id, SpecRunStatus status, int repositoryId = 1, int issue = 10, string? failure = null) =>
        ApiData.SpecRun(id, repositoryId, status, 1) with { ParentIssueNumber = issue, Title = $"Spec {issue}", FailureReason = failure };

    [Fact]
    public void counters_are_plain_tiles_and_needs_attention_is_highlighted_only_when_positive()
    {
        IRenderedComponent<DashboardOverview> none = Render<DashboardOverview>();
        Assert.Empty(none.FindAll("[data-stat] a, [data-stat] button"));
        Assert.DoesNotContain("border-warning", none.Find("[data-stat=NeedsAttention]").ClassName);

        Runs.SpecRuns.Add(Run("r-attn", SpecRunStatus.NeedsAttention));
        IRenderedComponent<DashboardOverview> some = Render<DashboardOverview>();
        Assert.Contains("border-warning", some.Find("[data-stat=NeedsAttention]").ClassName);
    }

    [Fact]
    public void a_single_repository_lane_uses_the_full_width()
    {
        Repositories.Repositories.RemoveAt(1);

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        Assert.Single(cut.FindAll("h1"));
        Assert.DoesNotContain("col-xl-6", cut.Markup);
    }

    [Fact]
    public void shows_an_alert_for_every_spec_that_is_awaiting_merge()
    {
        Runs.SpecRuns.Add(Run("r-merge", SpecRunStatus.AwaitingMerge, issue: 21));
        Runs.SpecRuns.Add(Run("r-ready", SpecRunStatus.ReadyForReview, repositoryId: 2, issue: 22));

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        var alerts = cut.FindAll("[data-alert=AwaitingMerge]");
        Assert.Equal(2, alerts.Count);
        Assert.Contains("acme/widgets", alerts[0].TextContent);
        Assert.Contains("#21", alerts[0].TextContent);
        Assert.Equal("/runs/r-merge", alerts[0].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void shows_a_prominent_alert_with_the_reason_for_specs_that_need_attention()
    {
        Runs.SpecRuns.Add(Run("r-attn", SpecRunStatus.NeedsAttention, issue: 23, failure: "Parent review cycle limit reached"));

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        var alert = Assert.Single(cut.FindAll("[data-alert=NeedsAttention]"));
        Assert.Contains("alert-warning", alert.ClassName);
        Assert.Contains("#23", alert.TextContent);
        Assert.Contains("Parent review cycle limit reached", alert.TextContent);
    }

    [Fact]
    public void renders_one_lane_per_repository_with_running_specs_and_slot_usage()
    {
        Runs.SpecRuns.Add(Run("r-1", SpecRunStatus.Running, issue: 11));
        Runs.SpecRuns.Add(Run("r-2", SpecRunStatus.Testing, issue: 12));
        Runs.SpecRuns.Add(Run("r-3", SpecRunStatus.Queued, issue: 13));
        Runs.SpecRuns.Add(Run("r-g", SpecRunStatus.Running, repositoryId: 2, issue: 14));

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        var widgets = cut.Find("[data-repo-lane='1']");
        Assert.Contains("acme/widgets", widgets.TextContent);
        Assert.Equal("2 / 2", widgets.QuerySelector("[data-testid=slots]")!.TextContent.Trim());
        Assert.Equal(["r-1", "r-2", "r-3"], widgets.QuerySelectorAll("[data-run-id]").Select(row => row.GetAttribute("data-run-id")!).ToArray());
        Assert.Equal("1 / 2", cut.Find("[data-repo-lane='2'] [data-testid=slots]").TextContent.Trim());
        Assert.Equal("Mode: Wait for merge", widgets.QuerySelector("[data-testid=dependency-mode]")!.TextContent.Trim());
    }

    [Fact]
    public void summarises_the_lanes_across_all_repositories()
    {
        Runs.SpecRuns.Add(Run("a", SpecRunStatus.Running));
        Runs.SpecRuns.Add(Run("b", SpecRunStatus.Running, repositoryId: 2));
        Runs.SpecRuns.Add(Run("c", SpecRunStatus.Queued));
        Runs.SpecRuns.Add(Run("d", SpecRunStatus.AwaitingMerge));
        Runs.SpecRuns.Add(Run("e", SpecRunStatus.NeedsAttention));
        Runs.SpecRuns.Add(Run("f", SpecRunStatus.Completed));
        Runs.SpecRuns.Add(Run("g", SpecRunStatus.Completed, repositoryId: 2));

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        Assert.Equal("2", cut.Find("[data-testid=count-Active]").TextContent.Trim());
        Assert.Equal("1", cut.Find("[data-testid=count-Waiting]").TextContent.Trim());
        Assert.Equal("1", cut.Find("[data-testid=count-AwaitingMerge]").TextContent.Trim());
        Assert.Equal("1", cut.Find("[data-testid=count-NeedsAttention]").TextContent.Trim());
        Assert.Equal("2", cut.Find("[data-testid=count-Completed]").TextContent.Trim());
    }

    [Fact]
    public void completed_specs_are_only_counted_inside_the_lane()
    {
        Runs.SpecRuns.Add(Run("done-1", SpecRunStatus.Completed));
        Runs.SpecRuns.Add(Run("done-2", SpecRunStatus.Completed));

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        Assert.Empty(cut.FindAll("[data-repo-lane='1'] [data-run-id]"));
        Assert.Contains("2 completed", cut.Find("[data-repo-lane='1'] [data-testid=completed-count]").TextContent);
    }

    [Fact]
    public void marks_the_current_repository_and_disabled_ones()
    {
        Repositories.Repositories[1] = ApiData.Repository(2, "gadgets", enabled: false);
        Selection.Select(1);

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        Assert.Contains("Current", cut.Find("[data-repo-lane='1']").TextContent);
        Assert.DoesNotContain("Current", cut.Find("[data-repo-lane='2']").TextContent);
        Assert.Contains("Disabled", cut.Find("[data-repo-lane='2']").TextContent);
    }

    [Fact]
    public void opening_a_queue_is_a_link_that_switches_the_context_without_touching_scheduling()
    {
        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        var link = cut.Find("[data-repo-lane='2'] a[data-testid=open-queue]");
        Assert.Equal("/queue", link.GetAttribute("href"));
        link.Click();

        Assert.Equal(2, Selection.CurrentRepositoryId);
        Assert.Empty(Enqueuer.Calls);
    }

    [Fact]
    public void shows_a_register_hint_without_repositories()
    {
        Repositories.Repositories.Clear();

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        Assert.Contains("No repositories", cut.Markup);
        Assert.Equal("/repositories", cut.Find("a[href='/repositories']").GetAttribute("href"));
    }

    [Fact]
    public async Task live_event_moves_a_spec_between_lanes_and_raises_the_alert()
    {
        Runs.SpecRuns.Add(Run("r-1", SpecRunStatus.Running, issue: 31));
        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();
        Assert.Empty(cut.FindAll("[data-alert=AwaitingMerge]"));

        Runs.SpecRuns[0] = Runs.SpecRuns[0] with { Status = SpecRunStatus.AwaitingMerge };
        await EventBus.PublishAsync(SpecStatusChanged("r-1", 1, SpecRunStatus.AwaitingMerge), CancellationToken.None);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-alert=AwaitingMerge]")));
        Assert.Equal("0", cut.Find("[data-testid=count-Active]").TextContent.Trim());
    }

    [Fact]
    public async Task ignores_events_that_do_not_change_spec_status()
    {
        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();
        Runs.SpecRuns.Add(Run("r-new", SpecRunStatus.Running));

        await EventBus.PublishAsync(new(5, new FrontierReconciliationRequested(new RunId("r-new"), ApiData.Now)), CancellationToken.None);
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal("0", cut.Find("[data-testid=count-Active]").TextContent.Trim());
    }

    [Fact]
    public void the_attention_alert_shows_the_summary_and_the_call_to_action_instead_of_the_raw_failure()
    {
        Runs.SpecRuns.Add(Run("r-you", SpecRunStatus.NeedsAttention, issue: 23, failure: "fatal: raw git error") with { Attention = AttentionReasons.BaseBranchMissing("develop", "acme/widgets", "ref missing") });
        Runs.SpecRuns.Add(Run("r-decide", SpecRunStatus.NeedsAttention, issue: 24, failure: "cycles") with { Attention = AttentionData.RunReason() });

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        var alerts = cut.FindAll("[data-alert=NeedsAttention]");
        Assert.Equal(2, alerts.Count);
        Assert.Contains("The base branch 'develop' does not exist on GitHub.", alerts[0].QuerySelector("[data-testid=alert-summary]")!.TextContent);
        Assert.DoesNotContain("raw git error", alerts[0].TextContent);
        Assert.Contains("Fix and continue", alerts[0].QuerySelector("[data-testid=alert-action]")!.TextContent);
        Assert.Equal("/runs/r-you", alerts[0].QuerySelector("[data-testid=alert-action]")!.GetAttribute("href"));
        Assert.Contains("Retry", alerts[1].QuerySelector("[data-testid=alert-action]")!.TextContent);
        Assert.Equal("2", cut.Find("[data-testid=count-NeedsAttention]").TextContent.Trim());
    }

    [Fact]
    public void the_attention_alert_says_when_webdevloop_is_still_fixing_it()
    {
        Runs.SpecRuns.Add(Run("r-auto", SpecRunStatus.NeedsAttention, issue: 25) with { Attention = AttentionReasons.IntegrationBranchExists("integration/r", "exists") });

        IRenderedComponent<DashboardOverview> cut = Render<DashboardOverview>();

        Assert.Contains("WebDevLoop is on it", cut.Find("[data-alert=NeedsAttention] [data-testid=alert-action]").TextContent);
    }
}
