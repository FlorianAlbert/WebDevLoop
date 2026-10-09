using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;
using WebDevLoop.Web.Resources.Prompts;

namespace WebDevLoop.Web.Tests.Resources.Prompts;

public sealed class EmbeddedDefaultPromptTemplatesTests
{
    private readonly EmbeddedDefaultPromptTemplates _templates = new();

    public static TheoryData<AgentRole> Roles => new(Enum.GetValues<AgentRole>());

    [Theory]
    [MemberData(nameof(Roles))]
    public void every_role_ships_a_template_valid_for_that_role(AgentRole role)
    {
        string template = _templates.GetTemplate(role);

        Assert.False(string.IsNullOrWhiteSpace(template));
        Assert.Empty(PromptTemplateValidator.Validate(role, template));
    }

    [Fact]
    public void shipped_templates_build_embedded_default_settings()
    {
        EffectiveSettings defaults = DefaultSettings.Create(_templates, "/data");

        Assert.Equal(_templates.GetTemplate(AgentRole.Tester), defaults.For(AgentRole.Tester).PromptTemplate);
    }

    [Theory]
    [InlineData(AgentRole.Explorer, "report_exploration")]
    [InlineData(AgentRole.Implementer, "report_implementation")]
    [InlineData(AgentRole.ReviewerCodingStandards, "report_review")]
    [InlineData(AgentRole.ReviewerSpecification, "report_review")]
    [InlineData(AgentRole.ConflictResolver, "report_conflict_resolution")]
    [InlineData(AgentRole.Tester, "report_test")]
    public void template_instructs_agent_to_report_through_its_structured_report_tool(AgentRole role, string reportTool)
    {
        Assert.Contains($"`{reportTool}`", _templates.GetTemplate(role));
    }

    [Theory]
    [InlineData(AgentRole.Implementer, "`tdd`")]
    [InlineData(AgentRole.ReviewerCodingStandards, "`code-review`")]
    [InlineData(AgentRole.ReviewerSpecification, "`code-review`")]
    [InlineData(AgentRole.Tester, "`playwright-cli`")]
    public void template_points_to_the_bundled_skill_its_workflow_step_uses(AgentRole role, string skill)
    {
        Assert.Contains(skill, _templates.GetTemplate(role));
    }

    [Theory]
    [InlineData(AgentRole.Explorer, new[] { "parent_spec_body", "spec_tickets", "exploration_notes_path", "worktree_path" })]
    [InlineData(AgentRole.Implementer, new[] { "ticket_body", "worktree_path", "branch_name", "integration_branch", "integration_tip_sha", "review_findings_json", "exploration_notes_path" })]
    [InlineData(AgentRole.ReviewerCodingStandards, new[] { "review_scope", "diff_base_ref", "diff_head_ref", "changed_files", "worktree_path" })]
    [InlineData(AgentRole.ReviewerSpecification, new[] { "review_scope", "ticket_body", "parent_spec_body", "diff_base_ref", "diff_head_ref" })]
    [InlineData(AgentRole.ConflictResolver, new[] { "conflicting_files", "integration_branch", "integration_tip_sha", "worktree_path", "ticket_body" })]
    [InlineData(AgentRole.Tester, new[] { "parent_spec_body", "tester_instructions", "reserved_port", "app_url", "worktree_path" })]
    public void template_uses_the_context_its_role_needs(AgentRole role, string[] placeholders)
    {
        string template = _templates.GetTemplate(role);

        Assert.All(placeholders, placeholder => Assert.Contains($"{{{placeholder}}}", template));
    }
}
