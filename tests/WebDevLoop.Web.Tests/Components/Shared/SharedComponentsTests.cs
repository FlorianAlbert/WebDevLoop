using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Web.Components.Shared;

namespace WebDevLoop.Web.Tests.Components.Shared;

public sealed class SharedComponentsTests : BunitContext
{
    [Fact]
    public void page_header_renders_single_h1_description_and_actions()
    {
        IRenderedComponent<PageHeader> cut = Render<PageHeader>(p => p
            .Add(c => c.Title, "Queue")
            .Add(c => c.Description, "Specs waiting to run")
            .Add(c => c.Actions, (RenderFragment)(b => b.AddMarkupContent(0, "<button id=\"go\">Go</button>"))));

        Assert.Equal("Queue", Assert.Single(cut.FindAll("h1")).TextContent);
        Assert.Equal("Specs waiting to run", cut.Find(".page-header__description").TextContent);
        Assert.NotNull(cut.Find(".page-header__actions #go"));
    }

    [Fact]
    public void page_titles_use_the_app_name_suffix()
    {
        Assert.Equal("Queue · WebDevLoop", PageTitles.Format("Queue"));
        Assert.Equal("WebDevLoop", PageTitles.Format(null));
    }

    [Fact]
    public void section_card_labels_the_section_with_its_heading()
    {
        IRenderedComponent<SectionCard> cut = Render<SectionCard>(p => p
            .Add(c => c.Title, "General")
            .AddChildContent("<p id=\"body\">x</p>"));

        string heading = cut.Find("h2").Id!;
        Assert.Equal(heading, cut.Find("section").GetAttribute("aria-labelledby"));
        Assert.NotNull(cut.Find("#body"));
    }

    [Theory]
    [InlineData(StatusVariant.Success, "status-pill--success")]
    [InlineData(StatusVariant.Warning, "status-pill--warning")]
    [InlineData(StatusVariant.Danger, "status-pill--danger")]
    [InlineData(StatusVariant.Info, "status-pill--info")]
    [InlineData(StatusVariant.Neutral, "status-pill--neutral")]
    public void status_pill_applies_variant_class(StatusVariant variant, string expected)
    {
        IRenderedComponent<StatusPill> cut = Render<StatusPill>(p => p.Add(c => c.Variant, variant).AddChildContent("Ok"));

        Assert.Contains(expected, cut.Find(".status-pill").ClassList);
        Assert.Empty(cut.FindAll(".status-pill__dot"));
    }

    [Fact]
    public void status_pill_shows_a_decorative_dot_on_request()
    {
        IRenderedComponent<StatusPill> cut = Render<StatusPill>(p => p.Add(c => c.ShowDot, true).AddChildContent("Ok"));

        Assert.Equal("true", cut.Find(".status-pill__dot").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void status_variants_map_known_statuses()
    {
        Assert.Equal(StatusVariant.Success, StatusVariants.For("Integrated"));
        Assert.Equal(StatusVariant.Warning, StatusVariants.For("NeedsAttention"));
        Assert.Equal(StatusVariant.Danger, StatusVariants.For("Failed"));
        Assert.Equal(StatusVariant.Neutral, StatusVariants.For("Queued"));
    }

    [Fact]
    public void empty_state_renders_text_and_primary_action()
    {
        IRenderedComponent<EmptyState> cut = Render<EmptyState>(p => p
            .Add(c => c.Icon, "inbox")
            .Add(c => c.Title, "Nothing here")
            .Add(c => c.Text, "Add one to begin.")
            .Add(c => c.Action, (RenderFragment)(b => b.AddMarkupContent(0, "<a id=\"cta\" href=\"/x\">Add</a>"))));

        Assert.Equal("Nothing here", cut.Find(".empty-state__title").TextContent);
        Assert.Equal("Add one to begin.", cut.Find(".empty-state__text").TextContent);
        Assert.NotNull(cut.Find(".empty-state__action #cta"));
        Assert.NotNull(cut.Find("svg[aria-hidden=true]"));
    }

    [Fact]
    public void app_date_time_renders_relative_text_with_exact_tooltip_and_machine_readable_value()
    {
        StubTime time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(time);

        IRenderedComponent<AppDateTime> cut = Render<AppDateTime>(p => p.Add(c => c.Value, time.GetUtcNow().AddMinutes(-5)));

        var element = cut.Find("time");
        Assert.Equal("5 min ago", element.TextContent);
        Assert.Equal("2026-10-10 11:55", element.GetAttribute("title"));
        Assert.Equal("2026-10-10T11:55:00.0000000Z", element.GetAttribute("datetime"));
    }

    [Fact]
    public void app_date_time_absolute_mode_and_missing_value()
    {
        StubTime time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(time);

        IRenderedComponent<AppDateTime> absolute = Render<AppDateTime>(p => p
            .Add(c => c.Value, time.GetUtcNow().AddMinutes(-5)).Add(c => c.Mode, DateTimeDisplay.Absolute));
        Assert.Equal("2026-10-10 11:55", absolute.Find("time").TextContent);

        IRenderedComponent<AppDateTime> none = Render<AppDateTime>();
        Assert.Empty(none.FindAll("time"));
        Assert.Equal("—", none.Markup.Trim().Replace("<span class=\"text-body-secondary\">", "").Replace("</span>", ""));
    }

    [Fact]
    public void relative_format_falls_back_to_absolute_after_a_week()
    {
        DateTimeOffset now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("just now", DateTimeFormats.Relative(now.AddSeconds(-10), now, TimeZoneInfo.Utc));
        Assert.Equal("3 hours ago", DateTimeFormats.Relative(now.AddHours(-3), now, TimeZoneInfo.Utc));
        Assert.Equal("yesterday", DateTimeFormats.Relative(now.AddHours(-30), now, TimeZoneInfo.Utc));
        Assert.Equal("2026-09-01 12:00", DateTimeFormats.Relative(now.AddDays(-39), now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void icon_button_is_named_for_assistive_tech_and_raises_clicks()
    {
        int clicks = 0;
        IRenderedComponent<IconButton> cut = Render<IconButton>(p => p
            .Add(c => c.Icon, "trash").Add(c => c.Label, "Remove repository")
            .Add(c => c.OnClick, () => clicks++));

        var button = cut.Find("button");
        Assert.Equal("Remove repository", button.GetAttribute("aria-label"));
        Assert.Equal("Remove repository", button.GetAttribute("title"));
        button.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void icon_button_with_href_renders_a_link()
    {
        IRenderedComponent<IconButton> cut = Render<IconButton>(p => p
            .Add(c => c.Icon, "external").Add(c => c.Label, "Open on GitHub").Add(c => c.Href, "https://github.com"));

        Assert.Equal("https://github.com", cut.Find("a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void icon_renders_known_icons_and_ignores_unknown_names()
    {
        Assert.NotNull(Render<Icon>(p => p.Add(c => c.Name, "plus")).Find("svg"));
        Assert.Empty(Render<Icon>(p => p.Add(c => c.Name, "nope")).FindAll("svg"));
        Assert.All(IconPaths.Names, name => Assert.True(IconPaths.TryGet(name, out _)));
    }

    [Fact]
    public void display_names_are_readable()
    {
        Assert.Equal("Wait for merge", DisplayNames.For(SpecDependencyMode.WaitForMerge));
        Assert.Equal("Stack on top", DisplayNames.For(SpecDependencyMode.StackOnTop));
        Assert.Equal("Reviewer – coding standards", DisplayNames.For(AgentRole.ReviewerCodingStandards));
        Assert.Equal("Conflict resolver", DisplayNames.For(AgentRole.ConflictResolver));
        Assert.Equal("Tester", DisplayNames.For(AgentRole.Tester));
        Assert.Equal("Needs attention", DisplayNames.Humanize("NeedsAttention"));
        Assert.All(Enum.GetValues<SpecDependencyMode>(), m => Assert.NotEmpty(DisplayNames.Describe(m)));
    }

    [Fact]
    public void theme_toggle_reads_and_persists_the_choice()
    {
        JSInterop.Setup<string>("wdlTheme.get").SetResult("dark");
        JSInterop.SetupVoid("wdlTheme.set", "light");

        IRenderedComponent<ThemeToggle> cut = Render<ThemeToggle>();

        Assert.Equal("true", cut.Find("[data-theme=dark]").GetAttribute("aria-pressed"));
        cut.Find("[data-theme=light]").Click();
        Assert.Equal("true", cut.Find("[data-theme=light]").GetAttribute("aria-pressed"));
        JSInterop.VerifyInvoke("wdlTheme.set");
    }

    private sealed class StubTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Theory]
    [InlineData("AttentionRaised", "{\"code\":\"X\",\"summary\":\"The folder is dirty.\",\"cause\":\"You\"}", "Needs attention: The folder is dirty")]
    [InlineData("AttentionRaised", "{}", "Needs attention: something stopped the work")]
    [InlineData("AttentionRemediationAttempted", "{\"outcome\":\"Unresolved\",\"summary\":\"Reset failed.\"}", "WebDevLoop tried an automatic fix, but it did not solve it: Reset failed")]
    [InlineData("AttentionRemediationAttempted", "{\"outcome\":\"Resolved\",\"summary\":\"Reset the branch\"}", "WebDevLoop tried an automatic fix: Reset the branch")]
    [InlineData("AttentionAutoResolved", "{\"summary\":\"Removed 3 untracked files\",\"resume\":\"Retry\"}", "WebDevLoop fixed it by itself: Removed 3 untracked files and resumed the work")]
    [InlineData("AttentionAutoResolved", "{\"summary\":\"Nothing to keep\",\"resume\":\"SkipTicket\"}", "WebDevLoop fixed it by itself: Nothing to keep and skipped the ticket")]
    [InlineData("AttentionNeedsYou", "{\"summary\":\"Conflict.\",\"tried\":[\"a\",\"b\"]}", "WebDevLoop needs you: Conflict (it already tried 2 things)")]
    [InlineData("AttentionNeedsYou", "{\"summary\":\"Conflict.\",\"tried\":[\"a\"]}", "WebDevLoop needs you: Conflict (it already tried 1 thing)")]
    [InlineData("AttentionNeedsYou", "{\"summary\":\"Conflict.\",\"tried\":[]}", "WebDevLoop needs you: Conflict")]
    [InlineData("ControlRetry", "{\"action\":\"Retry\",\"status\":\"Implementing\",\"tickets\":[]}", "You retried it")]
    [InlineData("ControlAutoRetry", "{\"action\":\"AutoRetry\",\"status\":\"Implementing\",\"tickets\":[]}", "WebDevLoop retried it automatically")]
    [InlineData("ControlSkip", "{\"action\":\"Skip\",\"status\":\"Skipped\",\"tickets\":[\"t2\",\"t3\"]}", "You skipped the ticket and 2 tickets that depended on it")]
    [InlineData("ControlAutoSkip", "{\"action\":\"AutoSkip\",\"status\":\"Skipped\",\"tickets\":[]}", "WebDevLoop skipped the ticket automatically")]
    [InlineData("ControlAbort", "{\"action\":\"Abort\",\"status\":\"Aborted\",\"tickets\":[\"t2\"]}", "You aborted it and 1 ticket that depended on it")]
    public void run_events_of_the_attention_pipeline_are_described_in_plain_language(string type, string payload, string expected)
    {
        Assert.Equal(expected, DisplayNames.DescribeRunEvent(type, payload));
    }

    [Fact]
    public void unknown_or_malformed_run_events_fall_back_to_the_type_name()
    {
        Assert.Equal("SomethingNew", DisplayNames.DescribeRunEvent("SomethingNew", "{}"));
        Assert.Equal("AttentionRaised", DisplayNames.DescribeRunEvent("AttentionRaised", "not json"));
        Assert.Equal("SomethingNew", DisplayNames.DescribeRunEvent("SomethingNew", "[1]"));
    }
}
