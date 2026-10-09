using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;
using WebDevLoop.Web.Components.Settings;

namespace WebDevLoop.Web.Tests.Components.Settings;

public sealed class SettingsPageTests : BunitContext
{
    private readonly RecordingSettingsManager _manager = new();

    private async Task<IRenderedComponent<SettingsPage>> RenderAsync(int? repositoryId = null)
    {
        Services.AddSingleton<ISettingsManager>(_manager);
        Services.AddSingleton<WebDevLoop.Core.Queries.IRepositoryQueries>(new StubRepositoryQueries());
        Services.AddSingleton(SettingsTestData.Defaults);
        IRenderedComponent<SettingsPage> page = Render<SettingsPage>();
        if (repositoryId is { } id)
        {
            await page.Find("[data-testid=scope]").ChangeAsync(id.ToString());
        }

        return page;
    }

    private static IElement Field(IRenderedComponent<SettingsPage> page, string field) => page.Find($"[data-field='{field}']");

    private static string Origin(IRenderedComponent<SettingsPage> page, string field) =>
        Field(page, field).QuerySelector("[data-testid=origin]")!.TextContent.Trim();

    private static string InheritedText(IRenderedComponent<SettingsPage> page, string field) =>
        Field(page, field).QuerySelector("[data-testid=inherited-value]")!.TextContent;

    private static string[] Errors(IRenderedComponent<SettingsPage> page, string field) =>
        Field(page, field).QuerySelectorAll("[data-testid=error]").Select(error => error.TextContent.Trim()).ToArray();

    private static Task SaveAsync(IRenderedComponent<SettingsPage> page) => page.Find("[data-testid=save]").ClickAsync();

    [Fact]
    public async Task empty_per_repo_override_saves_null_and_continues_showing_the_effective_global_value()
    {
        _manager.Global = new SettingsProfileData { MaxRetries = 7 };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        Assert.Equal("Inherited", Origin(page, "MaxRetries"));
        Assert.Contains("7", InheritedText(page, "MaxRetries"));
        Assert.Equal(string.Empty, page.Find("[data-field='MaxRetries'] input").GetAttribute("value") ?? string.Empty);

        await SaveAsync(page);

        (int id, SettingsProfileData saved) = Assert.Single(_manager.SavedRepository);
        Assert.Equal(1, id);
        Assert.Null(saved.MaxRetries);
        Assert.Null(saved.BaseBranch);
        Assert.Null(saved.TesterRunInstructions);
        Assert.Null(saved.TestPortRange);
        Assert.True(saved.Roles is null or { Count: 0 });
        Assert.Equal("Inherited", Origin(page, "MaxRetries"));
        Assert.Contains("7", InheritedText(page, "MaxRetries"));
    }

    [Fact]
    public async Task the_repository_editor_offers_no_startup_scoped_directories_and_drops_a_legacy_override_on_save()
    {
        _manager.Repository = new SettingsProfileData { WorkspaceRootDirectory = "/legacy", CopilotBaseDirectory = "/legacy-copilot" };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        Assert.Empty(page.FindAll("[data-field='WorkspaceRootDirectory']"));
        Assert.Empty(page.FindAll("[data-field='CopilotBaseDirectory']"));

        await SaveAsync(page);

        SettingsProfileData saved = Assert.Single(_manager.SavedRepository).Data;
        Assert.Null(saved.WorkspaceRootDirectory);
        Assert.Null(saved.CopilotBaseDirectory);
    }

    [Fact]
    public async Task the_global_editor_explains_that_the_directories_take_effect_after_a_restart()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        Assert.NotNull(Field(page, "WorkspaceRootDirectory"));
        string note = page.Find("[data-testid=startup-directories-note]").TextContent;
        Assert.Contains("restart", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clones must be moved", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task an_overridden_repo_value_is_marked_and_reset_to_inherited_saves_null()
    {
        _manager.Global = new SettingsProfileData { MaxRetries = 7 };
        _manager.Repository = new SettingsProfileData { MaxRetries = 3 };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        Assert.Equal("Overridden", Origin(page, "MaxRetries"));
        Assert.Equal("3", page.Find("[data-field='MaxRetries'] input").GetAttribute("value"));

        await Field(page, "MaxRetries").QuerySelector("[data-testid=reset]")!.ClickAsync();

        Assert.Equal("Inherited", Origin(page, "MaxRetries"));
        await SaveAsync(page);
        Assert.Null(Assert.Single(_manager.SavedRepository).Data.MaxRetries);
    }

    [Fact]
    public async Task global_scope_shows_embedded_defaults_as_inherited_and_the_global_only_implementer_limit()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        Assert.Contains(SettingsTestData.Defaults.MaxRetries.ToString(), InheritedText(page, "MaxRetries"));
        Assert.Equal("Inherited", Origin(page, "MaxConcurrentImplementersGlobal"));
        Assert.Contains("41000", InheritedText(page, "TestPortRange"));
    }

    [Fact]
    public async Task the_global_only_implementer_limit_is_not_offered_for_a_repository()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 2);

        Assert.Empty(page.FindAll("[data-field='MaxConcurrentImplementersGlobal']"));
        Assert.NotEmpty(page.FindAll("[data-field='MaxConcurrentImplementersPerRepo']"));
    }

    [Fact]
    public async Task editing_the_non_role_settings_saves_them_to_the_global_layer()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='MaxRetries'] input").ChangeAsync("5");
        await page.Find("[data-field='MaxActiveSpecsPerRepo'] input").ChangeAsync("3");
        await page.Find("[data-field='MaxReviewIterations'] input").ChangeAsync("8");
        await page.Find("[data-field='MaxConcurrentImplementersGlobal'] input").ChangeAsync("6");
        await page.Find("[data-field='BaseBranch'] input").ChangeAsync("develop");
        await page.Find("[data-field='SpecDependencyMode'] select").ChangeAsync(nameof(SpecDependencyMode.StackOnTop));
        await page.Find("[data-field='TesterRunInstructions'] textarea").ChangeAsync("dotnet run --urls $APP_URL");
        await page.Find("[data-field='TestPortRange'] input[data-testid=port-start]").ChangeAsync("42000");
        await page.Find("[data-field='TestPortRange'] input[data-testid=port-end]").ChangeAsync("42100");
        await SaveAsync(page);

        SettingsProfileData saved = Assert.Single(_manager.SavedGlobal);
        Assert.Equal(5, saved.MaxRetries);
        Assert.Equal(3, saved.MaxActiveSpecsPerRepo);
        Assert.Equal(8, saved.MaxReviewIterations);
        Assert.Equal(6, saved.MaxConcurrentImplementersGlobal);
        Assert.Equal("develop", saved.BaseBranch);
        Assert.Equal(SpecDependencyMode.StackOnTop, saved.SpecDependencyMode);
        Assert.Equal("dotnet run --urls $APP_URL", saved.TesterRunInstructions);
        Assert.Equal(new PortRangeData(42000, 42100), saved.TestPortRange);
        Assert.Contains("saved", page.Find("[data-testid=save-status]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Overridden", Origin(page, "MaxRetries"));
    }

    [Fact]
    public async Task role_model_reasoning_effort_and_timeout_are_editable_and_show_the_inherited_values()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();
        RoleSettings tester = SettingsTestData.Defaults.For(AgentRole.Tester);
        Assert.Contains(tester.Model, InheritedText(page, "Roles.Tester.Model"));
        Assert.Contains(((int)tester.Timeout.TotalSeconds).ToString(), InheritedText(page, "Roles.Tester.TimeoutSeconds"));

        await page.Find("[data-field='Roles.Tester.Model'] input").ChangeAsync("gpt-test");
        await page.Find("[data-field='Roles.Tester.ReasoningEffort'] input").ChangeAsync("low");
        await page.Find("[data-field='Roles.Tester.TimeoutSeconds'] input").ChangeAsync("120");
        await SaveAsync(page);

        SettingsProfileData saved = Assert.Single(_manager.SavedGlobal);
        Assert.Equal(new RoleSettingsOverride("gpt-test", "low", null, 120), saved.Roles![AgentRole.Tester]);
        Assert.False(saved.Roles.ContainsKey(AgentRole.Implementer));
    }

    [Fact]
    public async Task prompt_editor_shows_the_available_placeholders_for_each_role()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        string explorer = page.Find("[data-role='Explorer'] [data-testid=placeholders]").TextContent;
        string implementer = page.Find("[data-role='Implementer'] [data-testid=placeholders]").TextContent;

        Assert.Contains("{repo_owner}", explorer);
        Assert.DoesNotContain("{ticket_title}", explorer);
        Assert.Contains("{ticket_title}", implementer);
        Assert.Contains("{review_findings_json}", implementer);
    }

    [Fact]
    public async Task an_unknown_prompt_placeholder_shows_a_validation_error_and_blocks_saving()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='Roles.Implementer.PromptTemplate'] textarea").InputAsync("Fix {ticket_title} using {bogus_value}");

        Assert.Contains(Errors(page, "Roles.Implementer.PromptTemplate"), error => error.Contains("Unknown placeholder {bogus_value}"));

        await SaveAsync(page);

        Assert.Empty(_manager.SavedGlobal);
        Assert.NotEmpty(page.FindAll("[data-testid=error-summary]"));
    }

    [Fact]
    public async Task a_placeholder_that_is_not_available_for_the_role_is_an_error()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='Roles.Explorer.PromptTemplate'] textarea").InputAsync("Explore {ticket_title}");

        Assert.Contains(Errors(page, "Roles.Explorer.PromptTemplate"), error => error.Contains("not available for the Explorer role"));
    }

    [Fact]
    public async Task a_valid_prompt_is_saved_as_a_role_override()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='Roles.Implementer.PromptTemplate'] textarea").InputAsync("Implement {ticket_title} in {worktree_path}");
        await SaveAsync(page);

        SettingsProfileData saved = Assert.Single(_manager.SavedGlobal);
        Assert.Equal("Implement {ticket_title} in {worktree_path}", saved.Roles![AgentRole.Implementer].PromptTemplate);
        Assert.Equal("Overridden", Origin(page, "Roles.Implementer.PromptTemplate"));
    }

    [Fact]
    public async Task an_untouched_prompt_shows_the_inherited_template_and_is_saved_as_null()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        Assert.Equal(SettingsTestData.DefaultTemplate, page.Find("[data-field='Roles.Tester.PromptTemplate'] textarea").GetAttribute("value"));
        Assert.Equal("Inherited", Origin(page, "Roles.Tester.PromptTemplate"));

        await SaveAsync(page);

        Assert.True(Assert.Single(_manager.SavedGlobal).Roles is null or { Count: 0 });
    }

    [Fact]
    public async Task reset_to_default_template_restores_the_embedded_template_in_the_global_layer()
    {
        _manager.Global = new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Implementer] = new(PromptTemplate: "Custom {repo_name}") },
        };
        IRenderedComponent<SettingsPage> page = await RenderAsync();
        Assert.Equal("Overridden", Origin(page, "Roles.Implementer.PromptTemplate"));

        await Field(page, "Roles.Implementer.PromptTemplate").QuerySelector("[data-testid=reset-default]")!.ClickAsync();

        Assert.Equal(SettingsTestData.DefaultTemplate, page.Find("[data-field='Roles.Implementer.PromptTemplate'] textarea").GetAttribute("value"));
        await SaveAsync(page);
        Assert.True(Assert.Single(_manager.SavedGlobal).Roles is null or { Count: 0 });
    }

    [Fact]
    public async Task repository_prompt_can_reset_to_the_inherited_global_template_or_to_the_embedded_default()
    {
        _manager.Global = new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Tester] = new(PromptTemplate: "Global {repo_name}") },
        };
        _manager.Repository = new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Tester] = new(PromptTemplate: "Repo {repo_name}") },
        };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);
        Assert.Equal("Overridden", Origin(page, "Roles.Tester.PromptTemplate"));

        await Field(page, "Roles.Tester.PromptTemplate").QuerySelector("[data-testid=reset]")!.ClickAsync();
        Assert.Equal("Global {repo_name}", page.Find("[data-field='Roles.Tester.PromptTemplate'] textarea").GetAttribute("value"));

        await Field(page, "Roles.Tester.PromptTemplate").QuerySelector("[data-testid=reset-default]")!.ClickAsync();
        Assert.Equal(SettingsTestData.DefaultTemplate, page.Find("[data-field='Roles.Tester.PromptTemplate'] textarea").GetAttribute("value"));
        await SaveAsync(page);

        Assert.Equal(SettingsTestData.DefaultTemplate, Assert.Single(_manager.SavedRepository).Data.Roles![AgentRole.Tester].PromptTemplate);
    }

    [Fact]
    public async Task validation_errors_from_the_settings_manager_are_shown_next_to_the_field()
    {
        _manager.SaveGlobalOverride = CommandResult<SettingsProfileData>.Invalid(
        [
            new SettingsValidationError("MaxRetries", "Must be at least 0, but was -1."),
            new SettingsValidationError("Roles.Tester.TimeoutSeconds", "Must be at least 1, but was 0."),
        ]);
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='MaxRetries'] input").ChangeAsync("-1");
        await SaveAsync(page);

        Assert.Contains("Must be at least 0", Assert.Single(Errors(page, "MaxRetries")));
        Assert.Contains("Must be at least 1", Assert.Single(Errors(page, "Roles.Tester.TimeoutSeconds")));
        Assert.DoesNotContain("saved", page.Find("[data-testid=save-status]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("-1", page.Find("[data-field='MaxRetries'] input").GetAttribute("value"));
    }

    [Fact]
    public async Task a_concurrent_modification_conflict_is_reported()
    {
        _manager.SaveGlobalOverride = CommandResult<SettingsProfileData>.Conflict("The settings were changed concurrently; reload and retry.");
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await SaveAsync(page);

        Assert.Contains("changed concurrently", page.Find("[data-testid=save-status]").TextContent);
    }

    [Fact]
    public async Task a_half_filled_port_range_is_reported_and_not_saved()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='TestPortRange'] input[data-testid=port-start]").ChangeAsync("42000");
        await SaveAsync(page);

        Assert.Empty(_manager.SavedGlobal);
        Assert.Single(Errors(page, "TestPortRange"));
    }

    [Fact]
    public async Task switching_back_to_global_loads_the_global_layer()
    {
        _manager.Global = new SettingsProfileData { MaxRetries = 7 };
        _manager.Repository = new SettingsProfileData { MaxRetries = 3 };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        await page.Find("[data-testid=scope]").ChangeAsync(string.Empty);

        Assert.Equal("7", page.Find("[data-field='MaxRetries'] input").GetAttribute("value"));
        Assert.Contains("Global", page.Find("[data-testid=scope-title]").TextContent);
    }

    [Fact]
    public async Task an_unknown_repository_shows_the_load_error_instead_of_a_form()
    {
        _manager.RepositoryLoadOverride = CommandResult<SettingsProfileData>.NotFound("Repository 1 does not exist.");
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        Assert.Contains("does not exist", page.Find("[data-testid=load-error]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=save]"));
    }
}
