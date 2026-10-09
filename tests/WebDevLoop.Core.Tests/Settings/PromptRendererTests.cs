using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Tests.Settings;

public sealed class PromptRendererTests
{
    private readonly PromptRenderer _renderer = new();

    [Fact]
    public void replaces_ticket_and_review_placeholders()
    {
        const string template =
            "Ticket #{ticket_issue_number}: {ticket_title}\n{ticket_body}\nFix round {review_iteration}/{max_review_iterations}: {review_findings_json}";
        var values = new Dictionary<string, string>
        {
            [PromptPlaceholders.TicketIssueNumber] = "42",
            [PromptPlaceholders.TicketTitle] = "Add login",
            [PromptPlaceholders.TicketBody] = "Users can log in.",
            [PromptPlaceholders.ReviewIteration] = "1",
            [PromptPlaceholders.MaxReviewIterations] = "5",
            [PromptPlaceholders.ReviewFindingsJson] = "[{\"id\":\"F1\"}]",
        };

        string prompt = _renderer.Render(AgentRole.Implementer, template, values);

        Assert.Equal("Ticket #42: Add login\nUsers can log in.\nFix round 1/5: [{\"id\":\"F1\"}]", prompt);
    }

    [Fact]
    public void replaces_review_axis_and_diff_placeholders()
    {
        const string template = "Axis {review_axis} ({review_scope}): git diff {diff_base_ref}...{diff_head_ref}";
        var values = new Dictionary<string, string>
        {
            [PromptPlaceholders.ReviewAxis] = "Specification",
            [PromptPlaceholders.ReviewScope] = "ticket",
            [PromptPlaceholders.DiffBaseRef] = "abc123",
            [PromptPlaceholders.DiffHeadRef] = "webdevloop/r1/ticket/7",
        };

        string prompt = _renderer.Render(AgentRole.ReviewerSpecification, template, values);

        Assert.Equal("Axis Specification (ticket): git diff abc123...webdevloop/r1/ticket/7", prompt);
    }

    [Fact]
    public void replaces_tester_and_port_placeholders()
    {
        const string template = "Run: {tester_instructions}\nListen on port {reserved_port}; open {app_url}.";
        var values = new Dictionary<string, string>
        {
            [PromptPlaceholders.TesterInstructions] = "dotnet run --project src/App",
            [PromptPlaceholders.ReservedPort] = "41007",
            [PromptPlaceholders.AppUrl] = "http://127.0.0.1:41007",
        };

        string prompt = _renderer.Render(AgentRole.Tester, template, values);

        Assert.Equal(
            "Run: dotnet run --project src/App\nListen on port 41007; open http://127.0.0.1:41007.",
            prompt);
    }

    [Fact]
    public void unknown_placeholder_throws_settings_validation_error()
    {
        const string template = "Implement {ticket_title} using {secret_token}.";
        var values = new Dictionary<string, string> { [PromptPlaceholders.TicketTitle] = "Add login" };

        var error = Assert.Throws<SettingsValidationException>(
            () => _renderer.Render(AgentRole.Implementer, template, values));

        SettingsValidationError single = Assert.Single(error.Errors);
        Assert.Contains("{secret_token}", single.Message);
    }

    [Fact]
    public void placeholder_not_supplied_for_role_throws_settings_validation_error()
    {
        const string template = "Implement {ticket_title} and open http://localhost:{reserved_port}.";
        var values = new Dictionary<string, string>
        {
            [PromptPlaceholders.TicketTitle] = "Add login",
            [PromptPlaceholders.ReservedPort] = "41000",
        };

        var error = Assert.Throws<SettingsValidationException>(
            () => _renderer.Render(AgentRole.Implementer, template, values));

        SettingsValidationError single = Assert.Single(error.Errors);
        Assert.Equal("Roles.Implementer.PromptTemplate", single.Field);
        Assert.Contains("{reserved_port}", single.Message);
    }

    [Fact]
    public void missing_value_for_used_placeholder_throws_prompt_rendering_error()
    {
        const string template = "Implement #{ticket_issue_number}: {ticket_title}.";
        var values = new Dictionary<string, string> { [PromptPlaceholders.TicketTitle] = "Add login" };

        var error = Assert.Throws<PromptRenderingException>(
            () => _renderer.Render(AgentRole.Implementer, template, values));

        Assert.Equal(["ticket_issue_number"], error.MissingPlaceholders);
    }

    [Fact]
    public void substituted_values_and_non_placeholder_braces_stay_literal()
    {
        const string template = "Report {\"verdict\": \"clean\"} for {ticket_title}:\n{ticket_body}";
        var values = new Dictionary<string, string>
        {
            [PromptPlaceholders.TicketTitle] = "Braces",
            [PromptPlaceholders.TicketBody] = "Body mentions {ticket_title} and { } literally.",
        };

        string prompt = _renderer.Render(AgentRole.Implementer, template, values);

        Assert.Equal(
            "Report {\"verdict\": \"clean\"} for Braces:\nBody mentions {ticket_title} and { } literally.",
            prompt);
    }
}
