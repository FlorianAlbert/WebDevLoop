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
        Assert.Equal(new UpdateRepositoryCommand("develop", "https://github.com/acme/widgets.git", null, true), command);
    }

    [Fact]
    public void shows_validation_errors_of_the_edit_form()
    {
        Registry.UpdateResult = CommandResult<RepositoryView>.Invalid([new SettingsValidationError("CloneUrl", "must be an https URL")]);
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=edit-1]").Click();
        cut.Find("[data-testid=edit-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains("CloneUrl: must be an https URL", cut.Find("[data-testid=edit-errors]").TextContent));
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

    [Fact]
    public void remove_confirmation_spells_out_the_consequences()
    {
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=remove-1]").Click();

        string text = cut.Find("[data-testid=remove-confirmation]").TextContent;
        Assert.Contains("local clone is deleted", text);
        Assert.Contains("queued specs", text);
        Assert.Equal("Remove acme/widgets", cut.Find("[data-testid=remove-1]").GetAttribute("aria-label"));
    }

    [Fact]
    public void edit_form_has_associated_labels_and_a_heading()
    {
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.Find("[data-testid=edit-1]").Click();

        Assert.Equal("Editing acme/widgets", cut.Find("[data-testid=edit-heading]").TextContent);
        Assert.NotNull(cut.Find("label[for=edit-branch-1]"));
        Assert.Equal("edit-branch-1", cut.Find("input[name=defaultBaseBranch]").Id);
        Assert.Equal("edit-clone-1", cut.Find("input[name=cloneUrl]").Id);
    }

    [Fact]
    public void the_success_message_is_cleared_by_the_next_action()
    {
        Registry.UpdateResult = CommandResult<RepositoryView>.Succeeded(ApiData.Repository(1, "widgets", enabled: false));
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();
        cut.Find("[data-testid=toggle-1]").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=page-message]")));

        cut.Find("[data-testid=edit-2]").Click();

        Assert.Empty(cut.FindAll("[data-testid=page-message]"));
    }
}
