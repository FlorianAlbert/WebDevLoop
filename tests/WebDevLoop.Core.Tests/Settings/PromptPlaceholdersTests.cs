using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Tests.Settings;

public sealed class PromptPlaceholdersTests
{
    private static readonly string[] ArchitecturePlaceholders =
    [
        "repo_owner", "repo_name", "repo_url", "base_branch", "integration_branch", "integration_tip_sha",
        "workspace_root", "skills_root", "run_id", "attempt", "parent_spec_issue_number", "parent_spec_title",
        "parent_spec_body", "ticket_issue_number", "ticket_title", "ticket_body", "ticket_dependencies",
        "worktree_path", "branch_name", "diff_base_ref", "diff_head_ref", "changed_files", "review_axis",
        "review_findings_json", "max_review_iterations", "tester_instructions", "reserved_port", "app_url",
        "exploration_notes_path",
    ];

    [Fact]
    public void every_architecture_placeholder_is_known_and_available_to_some_role()
    {
        Assert.All(ArchitecturePlaceholders, name =>
        {
            Assert.True(PromptPlaceholders.IsKnown(name), name);
            Assert.Contains(Enum.GetValues<AgentRole>(), role => PromptPlaceholders.AvailableFor(role).Contains(name));
        });
    }

    [Fact]
    public void port_placeholders_are_available_to_the_tester_only()
    {
        AgentRole[] rolesWithPort = Enum.GetValues<AgentRole>()
            .Where(role => PromptPlaceholders.AvailableFor(role).Contains(PromptPlaceholders.ReservedPort))
            .ToArray();

        Assert.Equal([AgentRole.Tester], rolesWithPort);
    }

    [Fact]
    public void every_known_placeholder_has_a_description()
    {
        Assert.All(PromptPlaceholders.All, name => Assert.False(string.IsNullOrWhiteSpace(PromptPlaceholders.Descriptions[name])));
    }
}
