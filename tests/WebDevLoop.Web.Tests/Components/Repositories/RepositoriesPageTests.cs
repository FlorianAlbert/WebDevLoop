using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;
using WebDevLoop.Core.Settings;
using WebDevLoop.Web.Components.Repositories;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.Tests.Components.Dashboard;

namespace WebDevLoop.Web.Tests.Components.Repositories;

public sealed class RepositoriesPageTests : UiTestContext
{
    public RepositoriesPageTests()
    {
        Repositories.Repositories.Add(ApiData.Repository(1, "widgets"));
        Repositories.Repositories.Add(ApiData.Repository(2, "gadgets", enabled: false));
    }

    [Fact]
    public void lists_repositories_and_marks_the_current_one()
    {
        Selection.Select(1);

        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        Assert.Equal(2, cut.FindAll("tr[data-repo-id]").Count);
        Assert.Contains("acme/widgets", cut.Find("[data-repo-id='1']").TextContent);
        Assert.Contains("table-primary", cut.Find("[data-repo-id='1']").ClassName);
        Assert.Contains("Current", cut.Find("[data-repo-id='1']").TextContent);
        Assert.Contains("Disabled", cut.Find("[data-repo-id='2']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=select-1]"));
    }

    [Fact]
    public void selecting_a_repository_only_changes_the_ui_context()
    {
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=select-2]").Click();

        Assert.Equal(2, Selection.CurrentRepositoryId);
        Assert.Empty(Enqueuer.Calls);
        Assert.Empty(Registry.Registered);
        Assert.Empty(Registry.Updated);
        Assert.Empty(Registry.Removed);
        cut.WaitForAssertion(() => Assert.Contains("table-primary", cut.Find("[data-repo-id='2']").ClassName));
    }

    [Fact]
    public void registers_a_repository_with_blank_optional_fields_as_null_and_selects_it_when_none_is_current()
    {
        Registry.RegisterResult = CommandResult<RepositoryView>.Succeeded(ApiData.Repository(3, "tools"));
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=show-register]").Click();
        cut.Find("input[name=owner]").Change(" acme ");
        cut.Find("input[name=name]").Change("tools");
        cut.Find("input[name=localPath]").Change("/work/acme/tools");
        cut.Find("[data-testid=register-form]").Submit();

        cut.WaitForAssertion(() => Assert.Equal(3, Selection.CurrentRepositoryId));
        RegisterRepositoryCommand command = Assert.Single(Registry.Registered);
        Assert.Equal(new RegisterRepositoryCommand("acme", "tools", "/work/acme/tools", null, null), command);
        Assert.Empty(cut.FindAll("[data-testid=register-form]"));
    }

    [Fact]
    public void registering_keeps_the_current_repository_when_one_is_selected()
    {
        Selection.Select(1);
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=show-register]").Click();
        cut.Find("input[name=owner]").Change("acme");
        cut.Find("input[name=name]").Change("tools");
        cut.Find("input[name=localPath]").Change("/work/acme/tools");
        cut.Find("[data-testid=register-form]").Submit();

        cut.WaitForAssertion(() => Assert.Single(Registry.Registered));
        Assert.Equal(1, Selection.CurrentRepositoryId);
    }

    [Fact]
    public void shows_validation_errors_and_keeps_the_form_open()
    {
        Registry.RegisterResult = CommandResult<RepositoryView>.Invalid([new SettingsValidationError("Owner", "is required")]);
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=show-register]").Click();
        cut.Find("[data-testid=register-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Owner: is required", cut.Find("[data-testid=register-errors]").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-testid=register-form]"));
        Assert.Null(Selection.CurrentRepositoryId);
    }

    [Fact]
    public void shows_the_conflict_message_for_a_duplicate_repository()
    {
        Registry.RegisterResult = CommandResult<RepositoryView>.Conflict("acme/widgets is already registered");
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=show-register]").Click();
        cut.Find("[data-testid=register-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains("already registered", cut.Find("[data-testid=register-errors]").TextContent));
    }

    [Fact]
    public void edits_a_repository_through_the_inline_form()
    {
        Registry.UpdateResult = CommandResult<RepositoryView>.Succeeded(ApiData.Repository(1, "widgets"));
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=edit-1]").Click();
        Assert.Equal("main", cut.Find("input[name=defaultBaseBranch]").GetAttribute("value"));
        cut.Find("input[name=defaultBaseBranch]").Change("develop");
        cut.Find("[data-testid=edit-form]").Submit();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=edit-form]")));
        (int id, UpdateRepositoryCommand command) = Assert.Single(Registry.Updated);
        Assert.Equal(1, id);
        Assert.Equal(new UpdateRepositoryCommand("develop", "https://github.com/acme/widgets.git", "/work/acme/widgets", true), command);
    }

    [Fact]
    public void shows_validation_errors_of_the_edit_form()
    {
        Registry.UpdateResult = CommandResult<RepositoryView>.Invalid([new SettingsValidationError("LocalPath", "must be absolute")]);
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=edit-1]").Click();
        cut.Find("[data-testid=edit-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains("LocalPath: must be absolute", cut.Find("[data-testid=edit-errors]").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-testid=edit-form]"));
    }

    [Fact]
    public void toggling_enabled_sends_only_the_flag()
    {
        Registry.UpdateResult = CommandResult<RepositoryView>.Succeeded(ApiData.Repository(1, "widgets", enabled: false));
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=toggle-1]").Click();

        cut.WaitForAssertion(() => Assert.Single(Registry.Updated));
        Assert.Equal((1, new UpdateRepositoryCommand(IsEnabled: false)), Registry.Updated[0]);
    }

    [Fact]
    public void removal_needs_confirmation_and_clears_the_selection_when_the_current_repository_goes()
    {
        Selection.Select(1);
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=remove-1]").Click();
        Assert.Empty(Registry.Removed);
        cut.Find("[data-testid=confirm-remove-1]").Click();

        cut.WaitForAssertion(() => Assert.Equal([1], Registry.Removed));
        Assert.Null(Selection.CurrentRepositoryId);
    }

    [Fact]
    public void removal_can_be_cancelled()
    {
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=remove-1]").Click();
        cut.Find("[data-testid=cancel-remove-1]").Click();

        Assert.Empty(cut.FindAll("[data-testid=confirm-remove-1]"));
        Assert.Empty(Registry.Removed);
    }

    [Fact]
    public void explains_why_a_repository_with_history_cannot_be_removed()
    {
        Registry.RemoveResult = CommandResult<int>.Conflict("Repository 1 has spec runs; disable it instead");
        Selection.Select(1);
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=remove-1]").Click();
        cut.Find("[data-testid=confirm-remove-1]").Click();

        cut.WaitForAssertion(() => Assert.Contains("disable it instead", cut.Find("[data-testid=page-message]").TextContent));
        Assert.Equal(1, Selection.CurrentRepositoryId);
    }

    [Fact]
    public void shows_an_empty_state_without_repositories()
    {
        Repositories.Repositories.Clear();

        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        Assert.Contains("No repositories registered", cut.Markup);
    }
}
