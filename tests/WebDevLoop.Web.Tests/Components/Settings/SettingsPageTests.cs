using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;
using Microsoft.AspNetCore.Components;
using WebDevLoop.Web.Components.Repositories;
using WebDevLoop.Web.Components.Settings;

namespace WebDevLoop.Web.Tests.Components.Settings;

public sealed class SettingsPageTests : BunitContext
{
    private readonly RecordingSettingsManager _manager = new();

    private async Task<IRenderedComponent<SettingsPage>> RenderAsync(int? repositoryId = null, string? query = null, int? currentRepositoryId = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISettingsManager>(_manager);
        Services.AddSingleton<WebDevLoop.Core.Queries.IRepositoryQueries>(new StubRepositoryQueries());
        Services.AddSingleton(SettingsTestData.Defaults);
        CurrentRepositorySelection selection = new();
        selection.Select(currentRepositoryId);
        Services.AddSingleton<ICurrentRepositorySelection>(selection);
        Services.AddSingleton<RepositoryContext>();
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        string? target = query ?? (repositoryId is { } id ? $"repo={id}" : "repo=global");
        navigation.NavigateTo($"/settings?{target}");
        return Render<SettingsPage>();
    }

    private static IElement Field(IRenderedComponent<SettingsPage> page, string field) => page.Find($"[data-field='{field}']");

    private static string Origin(IRenderedComponent<SettingsPage> page, string field) =>
        Field(page, field).QuerySelector("[data-testid=origin]")!.TextContent.Trim();

    private static string Placeholder(IRenderedComponent<SettingsPage> page, string field) =>
        Field(page, field).QuerySelector("input, textarea")!.GetAttribute("placeholder") ?? string.Empty;

    private static string InheritedText(IRenderedComponent<SettingsPage> page, string field) =>
        Field(page, field).QuerySelector("[data-testid=inherited-value]")!.TextContent;

    private static Task MakeDirtyAsync(IRenderedComponent<SettingsPage> page) =>
        page.Find("[data-field='BaseBranch'] input").ChangeAsync("edited");

    private static string[] Errors(IRenderedComponent<SettingsPage> page, string field) =>
        Field(page, field).QuerySelectorAll("[data-testid=error]").Select(error => error.TextContent.Trim()).ToArray();

    private static Task SaveAsync(IRenderedComponent<SettingsPage> page) => page.Find("[data-testid=save]").ClickAsync();

    [Fact]
    public async Task empty_per_repo_override_saves_null_and_continues_showing_the_effective_global_value()
    {
        _manager.Global = new SettingsProfileData { MaxRetries = 7 };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        Assert.Equal("Inherited", Origin(page, "MaxRetries"));
        Assert.Equal("7", Placeholder(page, "MaxRetries"));
        Assert.Equal(string.Empty, page.Find("[data-field='MaxRetries'] input").GetAttribute("value") ?? string.Empty);

        await MakeDirtyAsync(page);
        await SaveAsync(page);

        (int id, SettingsProfileData saved) = Assert.Single(_manager.SavedRepository);
        Assert.Equal(1, id);
        Assert.Null(saved.MaxRetries);
        Assert.Equal("edited", saved.BaseBranch);
        Assert.Null(saved.TesterRunInstructions);
        Assert.Null(saved.TestPortRange);
        Assert.True(saved.Roles is null or { Count: 0 });
        Assert.Equal("Inherited", Origin(page, "MaxRetries"));
        Assert.Equal("7", Placeholder(page, "MaxRetries"));
    }

    [Fact]
    public async Task the_repository_editor_offers_no_startup_scoped_directories_and_drops_a_legacy_override_on_save()
    {
        _manager.Repository = new SettingsProfileData { WorkspaceRootDirectory = "/legacy", CopilotBaseDirectory = "/legacy-copilot" };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        Assert.Empty(page.FindAll("[data-field='WorkspaceRootDirectory']"));
        Assert.Empty(page.FindAll("[data-field='CopilotBaseDirectory']"));

        await MakeDirtyAsync(page);
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
        IElement noteElement = page.Find("[data-testid=startup-directories-note]");
        Assert.Contains("alert-warning", noteElement.ClassName);
        string note = noteElement.TextContent;
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
    public async Task global_scope_shows_embedded_defaults_as_placeholders_without_any_inherit_ui()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        Assert.Equal(SettingsTestData.Defaults.MaxRetries.ToString(), Placeholder(page, "MaxRetries"));
        Assert.Empty(page.FindAll("[data-testid=origin]"));
        Assert.Empty(page.FindAll("[data-testid=reset]"));
        Assert.Empty(page.FindAll("[data-testid=inherited-value]"));
        Assert.Equal("41000", page.Find("[data-testid=port-start]").GetAttribute("placeholder"));
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
        Assert.Equal("MaxRetries", page.Find("[data-field='MaxRetries']").GetAttribute("data-field"));
    }

    [Fact]
    public async Task the_troubleshooter_switch_and_attempts_show_the_defaults_in_the_global_editor()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        Assert.Contains("Try to resolve problems automatically with an agent", Field(page, "TroubleshooterEnabled").TextContent);
        Assert.Contains("Troubleshooter attempts per problem", Field(page, "TroubleshooterMaxAttempts").TextContent);
        Assert.Equal("Default (On)", page.Find("[data-field='TroubleshooterEnabled'] select option[value='']").TextContent);
        Assert.Equal("2", Placeholder(page, "TroubleshooterMaxAttempts"));
        Assert.Contains("Every attempt costs model usage", page.Find("#section-troubleshooter").TextContent);
    }

    [Fact]
    public async Task the_repository_troubleshooter_switch_is_inherit_on_or_off_and_shows_the_global_value()
    {
        _manager.Global = new SettingsProfileData { TroubleshooterEnabled = false, TroubleshooterMaxAttempts = 4 };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        Assert.Equal("Inherited", Origin(page, "TroubleshooterEnabled"));
        Assert.Equal("Inherit (Off)", page.Find("[data-field='TroubleshooterEnabled'] select option[value='']").TextContent);
        Assert.Equal(["Inherit (Off)", "On", "Off"], page.FindAll("[data-field='TroubleshooterEnabled'] select option").Select(option => option.TextContent).ToArray());
        Assert.Equal("4", Placeholder(page, "TroubleshooterMaxAttempts"));

        await page.Find("[data-field='TroubleshooterEnabled'] select").ChangeAsync("true");
        await SaveAsync(page);

        Assert.True(Assert.Single(_manager.SavedRepository).Data.TroubleshooterEnabled);
    }

    [Fact]
    public async Task the_troubleshooter_values_are_saved_to_the_global_layer()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='TroubleshooterEnabled'] select").ChangeAsync("false");
        await page.Find("[data-field='TroubleshooterMaxAttempts'] input").ChangeAsync("3");
        await SaveAsync(page);

        SettingsProfileData saved = Assert.Single(_manager.SavedGlobal);
        Assert.False(saved.TroubleshooterEnabled);
        Assert.Equal(3, saved.TroubleshooterMaxAttempts);
    }

    [Fact]
    public async Task zero_troubleshooter_attempts_show_a_validation_message_and_block_saving()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='TroubleshooterMaxAttempts'] input").ChangeAsync("0");
        await SaveAsync(page);

        Assert.Contains(Errors(page, "TroubleshooterMaxAttempts"), error => error.Contains("Must be 1 or more"));
        Assert.Empty(_manager.SavedGlobal);
    }

    [Fact]
    public async Task the_troubleshooter_role_is_listed_and_editable_like_the_other_roles()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();
        RoleSettings troubleshooter = SettingsTestData.Defaults.For(AgentRole.Troubleshooter);

        Assert.Equal("Troubleshooter", page.Find("[data-role='Troubleshooter'] [data-testid=role-toggle]").TextContent.Trim());
        Assert.Equal(troubleshooter.Model, Placeholder(page, "Roles.Troubleshooter.Model"));
        Assert.Equal(((int)troubleshooter.Timeout.TotalSeconds).ToString(), Placeholder(page, "Roles.Troubleshooter.TimeoutSeconds"));
        Assert.Contains("{attention_code}", page.Find("[data-role='Troubleshooter'] [data-testid=placeholders]").TextContent);
        Assert.DoesNotContain("{app_url}", page.Find("[data-role='Troubleshooter'] [data-testid=placeholders]").TextContent);

        await page.Find("[data-field='Roles.Troubleshooter.Model'] input").ChangeAsync("gpt-fix");
        await page.Find("[data-field='Roles.Troubleshooter.ReasoningEffort'] select").ChangeAsync("xhigh");
        await page.Find("[data-field='Roles.Troubleshooter.TimeoutSeconds'] input").ChangeAsync("900");
        await SaveAsync(page);

        Assert.Equal(new RoleSettingsOverride("gpt-fix", "xhigh", null, 900), Assert.Single(_manager.SavedGlobal).Roles![AgentRole.Troubleshooter]);
    }

    [Fact]
    public async Task a_troubleshooter_prompt_with_a_placeholder_of_another_role_is_rejected()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-field='Roles.Troubleshooter.PromptTemplate'] textarea").InputAsync("Fix {attention_code} at {app_url}");

        Assert.Contains(Errors(page, "Roles.Troubleshooter.PromptTemplate"), error => error.Contains("not available for the Troubleshooter role"));

        await SaveAsync(page);

        Assert.Empty(_manager.SavedGlobal);
    }

    [Fact]
    public async Task role_model_reasoning_effort_and_timeout_are_editable_and_show_the_inherited_values()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();
        RoleSettings tester = SettingsTestData.Defaults.For(AgentRole.Tester);
        Assert.Equal(tester.Model, Placeholder(page, "Roles.Tester.Model"));
        Assert.Equal(((int)tester.Timeout.TotalSeconds).ToString(), Placeholder(page, "Roles.Tester.TimeoutSeconds"));
        Assert.Empty(page.FindAll("[data-field='Roles.Tester.ReasoningEffort'] datalist"));

        await page.Find("[data-field='Roles.Tester.Model'] input").ChangeAsync("gpt-test");
        await page.Find("[data-field='Roles.Tester.ReasoningEffort'] select").ChangeAsync("low");
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

        await Field(page, "Roles.Implementer.PromptTemplate").QuerySelector("[data-testid=override-prompt]")!.ClickAsync();
        await page.Find("[data-field='Roles.Implementer.PromptTemplate'] textarea").InputAsync("Implement {ticket_title} in {worktree_path}");
        await SaveAsync(page);

        SettingsProfileData saved = Assert.Single(_manager.SavedGlobal);
        Assert.Equal("Implement {ticket_title} in {worktree_path}", saved.Roles![AgentRole.Implementer].PromptTemplate);
    }

    [Fact]
    public async Task an_untouched_prompt_shows_the_inherited_template_and_is_saved_as_null()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        Assert.Equal(SettingsTestData.DefaultTemplate, page.Find("[data-field='Roles.Tester.PromptTemplate'] textarea").GetAttribute("value"));
        Assert.NotNull(page.Find("[data-field='Roles.Tester.PromptTemplate'] textarea").GetAttribute("readonly"));
        Assert.True(page.Find("[data-testid=save]").HasAttribute("disabled"));

        await Field(page, "Roles.Tester.PromptTemplate").QuerySelector("[data-testid=override-prompt]")!.ClickAsync();
        Assert.Null(page.Find("[data-field='Roles.Tester.PromptTemplate'] textarea").GetAttribute("readonly"));

        await MakeDirtyAsync(page);
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
        Assert.Null(page.Find("[data-field='Roles.Implementer.PromptTemplate'] textarea").GetAttribute("readonly"));

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

        await Field(page, "Roles.Tester.PromptTemplate").QuerySelector("[data-testid=override-prompt]")!.ClickAsync();
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

        await MakeDirtyAsync(page);
        await SaveAsync(page);

        Assert.Equal("Must be 0 or more.", Assert.Single(Errors(page, "MaxRetries")));
        Assert.Equal("Must be 1 or more.", Assert.Single(Errors(page, "Roles.Tester.TimeoutSeconds")));
        string summary = page.Find("[data-testid=error-summary]").TextContent;
        Assert.Contains("Max retries", summary);
        Assert.DoesNotContain("MaxRetries", summary);
        Assert.Equal("#f-MaxRetries", page.Find("[data-testid=error-summary] a").GetAttribute("href"));
        IElement input = page.Find("[data-field='MaxRetries'] input");
        Assert.Contains("is-invalid", input.ClassName);
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal("e-MaxRetries", input.GetAttribute("aria-describedby"));
        JSInterop.VerifyFocusAsyncInvoke();
        Assert.Equal("edited", page.Find("[data-field='BaseBranch'] input").GetAttribute("value"));
    }

    [Fact]
    public async Task a_concurrent_modification_conflict_is_reported()
    {
        _manager.SaveGlobalOverride = CommandResult<SettingsProfileData>.Conflict("The settings were changed concurrently; reload and retry.");
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await MakeDirtyAsync(page);
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
        Assert.Contains("Test port range", page.Find("[data-testid=error-summary]").TextContent);
    }

    [Fact]
    public async Task a_negative_number_is_rejected_in_plain_language_and_numeric_inputs_declare_their_range()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        IElement retries = page.Find("[data-field='MaxRetries'] input");
        Assert.Equal("0", retries.GetAttribute("min"));
        Assert.Equal("1024", page.Find("[data-testid=port-start]").GetAttribute("min"));
        Assert.Equal("65535", page.Find("[data-testid=port-end]").GetAttribute("max"));

        await retries.ChangeAsync("-1");
        await SaveAsync(page);

        Assert.Empty(_manager.SavedGlobal);
        Assert.Equal("Must be 0 or more.", Assert.Single(Errors(page, "MaxRetries")));
        Assert.Contains("Max retries: Must be 0 or more.", page.Find("[data-testid=error-summary]").TextContent);
    }

    [Fact]
    public async Task a_reversed_port_range_is_reported()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        await page.Find("[data-testid=port-start]").ChangeAsync("42100");
        await page.Find("[data-testid=port-end]").ChangeAsync("42000");
        await SaveAsync(page);

        Assert.Empty(_manager.SavedGlobal);
        Assert.Contains("first port must not be greater", Assert.Single(Errors(page, "TestPortRange")));
    }

    [Fact]
    public async Task the_save_bar_tracks_unsaved_changes_and_discard_restores_the_loaded_values()
    {
        _manager.Global = new SettingsProfileData { MaxRetries = 7 };
        IRenderedComponent<SettingsPage> page = await RenderAsync();
        Assert.True(page.Find("[data-testid=save]").HasAttribute("disabled"));
        Assert.True(page.Find("[data-testid=discard]").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-testid=dirty-note]"));

        await page.Find("[data-field='MaxRetries'] input").ChangeAsync("9");

        Assert.False(page.Find("[data-testid=save]").HasAttribute("disabled"));
        Assert.Contains("unsaved changes", page.Find("[data-testid=dirty-note]").TextContent);

        await page.Find("[data-testid=discard]").ClickAsync();

        Assert.Equal("7", page.Find("[data-field='MaxRetries'] input").GetAttribute("value"));
        Assert.True(page.Find("[data-testid=save]").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-testid=dirty-note]"));
    }

    [Fact]
    public async Task leaving_with_unsaved_changes_asks_for_confirmation()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();
        await MakeDirtyAsync(page);
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("/queue");

        Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "confirm");
        Assert.EndsWith("/settings?repo=global", navigation.Uri);
    }

    [Fact]
    public async Task the_scope_defaults_to_the_current_repository_and_follows_the_repo_query()
    {
        IRenderedComponent<SettingsPage> followsSelection = await RenderAsync(query: "x=1", currentRepositoryId: 2);
        Assert.Contains("Overrides for", followsSelection.Find("[data-testid=scope-title]").TextContent);
        Assert.Equal("2", followsSelection.Find("[data-testid=scope]").QuerySelector("option[selected]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task repo_global_in_the_query_overrides_the_current_repository()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync(query: "repo=global", currentRepositoryId: 2);

        Assert.Contains("Global settings", page.Find("[data-testid=scope-title]").TextContent);
    }

    [Fact]
    public async Task agent_roles_are_a_collapsed_accordion_with_readable_names()
    {
        IRenderedComponent<SettingsPage> page = await RenderAsync();

        IElement toggle = page.Find("[data-role='ReviewerCodingStandards'] [data-testid=role-toggle]");
        Assert.Equal("Reviewer – coding standards", toggle.TextContent.Trim());
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));

        await toggle.ClickAsync();

        Assert.Equal("true", page.Find("[data-role='ReviewerCodingStandards'] [data-testid=role-toggle]").GetAttribute("aria-expanded"));
        Assert.Contains("Wait for merge", page.Find("[data-field='SpecDependencyMode'] select").InnerHtml);
        Assert.NotEmpty(page.FindAll("[data-testid=section-nav] a"));
    }

    [Fact]
    public async Task switching_back_to_global_loads_the_global_layer()
    {
        _manager.Global = new SettingsProfileData { MaxRetries = 7 };
        _manager.Repository = new SettingsProfileData { MaxRetries = 3 };
        IRenderedComponent<SettingsPage> page = await RenderAsync(repositoryId: 1);

        await page.Find("[data-testid=scope]").ChangeAsync("global");

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
