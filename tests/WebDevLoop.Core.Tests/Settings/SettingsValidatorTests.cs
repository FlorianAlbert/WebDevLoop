using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Tests.Settings;

public sealed class SettingsValidatorTests
{
    private const int RepositoryId = 3;

    [Fact]
    public void fully_specified_valid_global_profile_has_no_errors()
    {
        SettingsProfile profile = SettingsProfile.ForGlobal();
        profile.MaxActiveSpecsPerRepo = 1;
        profile.MaxConcurrentImplementersGlobal = 4;
        profile.MaxConcurrentImplementersPerRepo = 2;
        profile.MaxReviewIterations = 5;
        profile.MaxRetries = 0;
        profile.ParentReviewCycleLimit = 3;
        profile.TesterCycleLimit = 3;
        profile.TestPortRange = new TestPortRange(41000, 41999);
        profile.SetRole(AgentRole.Tester, new RoleSettingsOverride(TimeoutSeconds: 600, PromptTemplate: "Open {app_url}."));

        Assert.Empty(SettingsValidator.Validate(profile));
    }

    [Fact]
    public void privileged_test_port_range_is_rejected()
    {
        SettingsProfile profile = SettingsProfile.ForGlobal();
        profile.TestPortRange = new TestPortRange(80, 8080);

        SettingsValidationError error = Assert.Single(SettingsValidator.Validate(profile));

        Assert.Equal(nameof(SettingsProfile.TestPortRange), error.Field);
    }

    [Fact]
    public void non_positive_limits_and_timeouts_are_rejected()
    {
        SettingsProfile profile = SettingsProfile.ForRepository(RepositoryId);
        profile.MaxActiveSpecsPerRepo = 0;
        profile.MaxConcurrentImplementersPerRepo = 0;
        profile.MaxReviewIterations = 0;
        profile.MaxRetries = -1;
        profile.ParentReviewCycleLimit = 0;
        profile.TesterCycleLimit = -2;
        profile.SetRole(AgentRole.Implementer, new RoleSettingsOverride(TimeoutSeconds: 0));

        string[] fields = SettingsValidator.Validate(profile).Select(error => error.Field).ToArray();

        Assert.Equal(
            [
                nameof(SettingsProfile.MaxActiveSpecsPerRepo),
                nameof(SettingsProfile.MaxConcurrentImplementersPerRepo),
                nameof(SettingsProfile.MaxReviewIterations),
                nameof(SettingsProfile.MaxRetries),
                nameof(SettingsProfile.ParentReviewCycleLimit),
                nameof(SettingsProfile.TesterCycleLimit),
                "Roles.Implementer.TimeoutSeconds",
            ],
            fields);
    }

    [Fact]
    public void repository_profile_cannot_set_global_only_implementer_limit()
    {
        SettingsProfile profile = SettingsProfile.ForRepository(RepositoryId);
        profile.MaxConcurrentImplementersGlobal = 8;

        SettingsValidationError error = Assert.Single(SettingsValidator.Validate(profile));

        Assert.Equal(nameof(SettingsProfile.MaxConcurrentImplementersGlobal), error.Field);
    }

    [Fact]
    public void role_prompt_template_with_unknown_placeholder_is_rejected()
    {
        SettingsProfile profile = SettingsProfile.ForGlobal();
        profile.SetRole(
            AgentRole.ReviewerSpecification,
            new RoleSettingsOverride(PromptTemplate: "Review {ticket_title} with {github_token}."));

        SettingsValidationError error = Assert.Single(SettingsValidator.Validate(profile));

        Assert.Equal("Roles.ReviewerSpecification.PromptTemplate", error.Field);
        Assert.Contains("{github_token}", error.Message);
    }

    [Fact]
    public void ensure_valid_throws_settings_validation_error_listing_all_problems()
    {
        SettingsProfile profile = SettingsProfile.ForGlobal();
        profile.MaxReviewIterations = 0;
        profile.TestPortRange = new TestPortRange(1, 2);

        var error = Assert.Throws<SettingsValidationException>(() => SettingsValidator.EnsureValid(profile));

        Assert.Equal(2, error.Errors.Count);
    }
}
