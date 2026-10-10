using Bunit;
using Microsoft.AspNetCore.Components;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Shared;
using WebDevLoop.Web.Components.Tickets;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Shared;

public sealed class AttentionCardTests
{
    private static IRenderedComponent<AttentionCard> Render(RunDetailHarness harness, AttentionReason reason, bool withControls = false) =>
        harness.Render<AttentionCard>(p =>
        {
            p.Add(c => c.Reason, reason);
            if (withControls)
            {
                p.AddChildContent<TicketRunControls>(c => c
                    .Add(x => x.Ticket, Views.Ticket("t1", 10, TicketRunStatus.NeedsAttention))
                    .Add(x => x.Attention, reason));
            }
        });

    [Fact]
    public void Shows_title_summary_why_it_matters_and_cause_chip()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full(AttentionCause.You));

        Assert.Equal("Action needed", cut.Find("h2").TextContent.Trim());
        Assert.Equal("The ticket's working folder has files that were not committed.", cut.Find("[data-testid=failure-reason]").TextContent.Trim());
        Assert.Contains("cannot verify a clean checkout", cut.Find("[data-testid=attention-why]").TextContent);
        Assert.Equal("You need to fix something", cut.Find("[data-testid=attention-cause]").TextContent.Trim());
        Assert.Equal("attention-title", cut.Find("section").GetAttribute("aria-labelledby"));
        Assert.Empty(cut.FindAll("[data-testid=attention-auto-fix]"));
    }

    [Theory]
    [InlineData(AttentionCause.WebDevLoop, "WebDevLoop got stuck")]
    [InlineData(AttentionCause.You, "You need to fix something")]
    [InlineData(AttentionCause.Decision, "Your decision")]
    public void Cause_chip_uses_plain_wording(AttentionCause cause, string expected)
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full(cause));

        Assert.Equal(expected, cut.Find("[data-testid=attention-cause]").TextContent.Trim());
        Assert.Equal(cause.ToString(), cut.Find("[data-testid=attention-card]").GetAttribute("data-cause"));
    }

    [Fact]
    public void Lists_what_webdevloop_already_tried_including_the_attempted_auto_fix()
    {
        using var harness = new RunDetailHarness();
        var autoFix = new AttentionAutoFix("Removed the untracked files", true, "The folder is clean again.");

        var cut = Render(harness, AttentionData.Full(autoFix: autoFix, tried: ["Retried the ticket 2 times"]));

        string[] tried = cut.FindAll("[data-testid=attention-tried] > li").Select(li => li.TextContent).ToArray();
        Assert.Equal(2, tried.Length);
        Assert.Contains("Retried the ticket 2 times", tried[0]);
        Assert.Contains("Removed the untracked files", tried[1]);
        Assert.Contains("The folder is clean again.", tried[1]);
    }

    [Fact]
    public void Hides_the_tried_section_when_nothing_was_tried()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full(tried: []));

        Assert.Empty(cut.FindAll("[data-testid=attention-tried]"));
        Assert.DoesNotContain("What WebDevLoop already tried", cut.Markup);
    }

    [Fact]
    public void User_steps_are_a_numbered_list_with_copyable_commands_and_links()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full());

        Assert.Contains("What you can do", cut.Markup);
        var steps = cut.FindAll("ol[data-testid=attention-steps] > li");
        Assert.Equal(4, steps.Count);
        Assert.Contains("Look at what is in the folder.", steps[0].TextContent);
        Assert.Equal(AttentionData.Command, cut.Find("[data-testid=attention-command] code").TextContent);
        Assert.Single(cut.FindAll("[data-testid=attention-command]"));
        Assert.Equal("Copy command: " + AttentionData.Command, cut.Find("[data-testid=copy-button]").GetAttribute("aria-label"));
    }

    [Fact]
    public void Copy_button_copies_the_command_through_the_clipboard_api_and_announces_it()
    {
        using var harness = new RunDetailHarness();
        harness.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render(harness, AttentionData.Full());

        cut.Find("[data-testid=copy-button]").Click();

        var call = Assert.Single(harness.JSInterop.Invocations, invocation => invocation.Identifier == "navigator.clipboard.writeText");
        Assert.Equal(AttentionData.Command, call.Arguments[0]);
        Assert.Contains("Copied", cut.Find("[data-testid=copy-button]").TextContent);
        Assert.Contains("Copied to the clipboard", cut.Find("[data-testid=copy-status]").TextContent);
    }

    [Fact]
    public void Copy_button_reports_a_failing_clipboard()
    {
        using var harness = new RunDetailHarness();
        harness.JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetException(new Microsoft.JSInterop.JSException("denied"));
        var cut = Render(harness, AttentionData.Full());

        cut.Find("[data-testid=copy-button]").Click();

        Assert.Contains("Copy failed", cut.Find("[data-testid=copy-button]").TextContent);
        Assert.Contains("Select the text", cut.Find("[data-testid=copy-status]").TextContent);
    }

    [Fact]
    public void In_app_links_stay_in_the_app_and_external_links_open_in_a_new_tab()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full());

        var links = cut.FindAll("[data-testid=attention-link]");
        Assert.Equal(2, links.Count);
        Assert.Equal("/settings#section-general", links[0].GetAttribute("href"));
        Assert.Null(links[0].GetAttribute("target"));
        Assert.Equal("https://github.com/acme/widgets/branches", links[1].GetAttribute("href"));
        Assert.Equal("_blank", links[1].GetAttribute("target"));
        Assert.Equal("noopener noreferrer", links[1].GetAttribute("rel"));
        Assert.Contains("opens in a new tab", links[1].TextContent);
    }

    [Fact]
    public void Technical_details_are_collapsed_and_hold_the_raw_text()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full());

        var details = cut.Find("details[data-testid=attention-details]");
        Assert.False(details.HasAttribute("open"));
        Assert.Equal("Technical details", details.QuerySelector("summary")!.TextContent.Trim());
        Assert.Contains("is dirty", cut.Find("[data-testid=attention-details-text]").TextContent);
    }

    [Fact]
    public void Every_button_has_a_visible_consequence_line_it_is_described_by()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full(), withControls: true);

        foreach (string key in new[] { "retry", "skip", "skip-dependents", "abort" })
        {
            var button = cut.Find($"[data-testid=attention-actions] [data-testid=control-{key}]");
            var line = cut.Find($"[data-testid=consequence-{key}]");
            Assert.Equal($"consequence-{key}", button.GetAttribute("aria-describedby"));
            Assert.Equal($"consequence-{key}", line.Id);
            Assert.False(string.IsNullOrWhiteSpace(line.TextContent));
        }

        Assert.Contains("Starts the failed phase again.", cut.Find("[data-testid=consequence-retry]").TextContent);
        Assert.Contains("Stops this ticket for good.", cut.Find("[data-testid=consequence-abort]").TextContent);
    }

    [Fact]
    public void Card_without_controls_is_read_only()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, AttentionData.Full());

        Assert.Empty(cut.FindAll("button[data-testid^=control-]"));
        Assert.Empty(cut.FindAll("[data-testid=attention-actions]"));
    }

    [Fact]
    public void A_pending_auto_fix_shows_a_calm_info_state_instead_of_asking_the_user()
    {
        using var harness = new RunDetailHarness();
        var reason = AttentionData.Pending();
        Assert.True(reason.AutoFixPending);

        var cut = Render(harness, reason, withControls: true);

        Assert.Equal("pending", cut.Find("[data-testid=attention-card]").GetAttribute("data-auto-fix"));
        Assert.Equal("Fixing automatically", cut.Find("h2").TextContent.Trim());
        Assert.Contains("WebDevLoop is trying to fix this automatically", cut.Find("[data-testid=attention-auto-fix]").TextContent);
        Assert.Equal(reason.Summary, cut.Find("[data-testid=failure-reason]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=attention-steps]"));
        Assert.DoesNotContain("What you can do", cut.Markup);
        Assert.False(cut.Find("details[data-testid=attention-actions-anyway]").HasAttribute("open"));
    }

    [Fact]
    public void Once_the_auto_fix_was_attempted_the_user_is_asked()
    {
        using var harness = new RunDetailHarness();
        var reason = AttentionData.Pending().WithAutoFixAttempted("The branch could not be reset.");

        var cut = Render(harness, reason, withControls: true);

        Assert.Empty(cut.FindAll("[data-testid=attention-auto-fix]"));
        Assert.Equal("Action needed", cut.Find("h2").TextContent.Trim());
        Assert.Contains("The branch could not be reset.", cut.Find("[data-testid=attention-auto-fix-outcome]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=attention-actions] [data-testid=control-retry]"));
    }

    [Fact]
    public void Real_catalogue_reason_renders_with_its_settings_link()
    {
        using var harness = new RunDetailHarness();
        var reason = AttentionReasons.BaseBranchMissing("develop", "acme/widgets", "ref refs/heads/develop not found");

        var cut = Render(harness, reason);

        Assert.Contains("develop", cut.Find("[data-testid=failure-reason]").TextContent);
        Assert.Contains(cut.FindAll("[data-testid=attention-link]"), link => link.GetAttribute("href") == "/settings#section-general");
    }

    [Fact]
    public void Display_helpers_pick_the_call_to_action()
    {
        Assert.Equal("Open", AttentionDisplay.CallToAction(null));
        Assert.Equal("Fix and continue", AttentionDisplay.CallToAction(AttentionData.Full(AttentionCause.You)));
        Assert.Equal("Retry", AttentionDisplay.CallToAction(AttentionData.Full(AttentionCause.Decision)));
        Assert.Equal("Open (WebDevLoop is on it)", AttentionDisplay.CallToAction(AttentionData.Pending()));
        Assert.True(AttentionDisplay.IsInApp("/settings#section-roles"));
        Assert.False(AttentionDisplay.IsInApp("//evil.example"));
        Assert.False(AttentionDisplay.IsInApp("https://github.com"));
        Assert.Equal("raw failure", AttentionDisplay.Headline(null, "raw failure"));
        Assert.Equal(AttentionData.Full().Summary, AttentionDisplay.Headline(AttentionData.Full(), "raw failure"));
        Assert.Equal(AttentionCode.Unclassified, AttentionDisplay.Resolve(null, "raw", forTicket: true).Code);
    }
}
