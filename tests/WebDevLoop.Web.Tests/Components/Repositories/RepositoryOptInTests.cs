using Bunit;
using WebDevLoop.Web.Components.Repositories;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.Tests.Components.Dashboard;

namespace WebDevLoop.Web.Tests.Components.Repositories;

public sealed class RepositoryOptInTests : UiTestContext
{
    public RepositoryOptInTests() => Repositories.Repositories.Add(ApiData.Repository());

    [Fact]
    public void missing_label_keeps_the_connection_and_uses_the_same_consent_dialog_to_resume()
    {
        Labels.Exists = false;
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        cut.WaitForAssertion(() => Assert.Contains("paused", cut.Find("[data-testid=label-status]").TextContent));
        cut.Find("[data-testid=review-label]").Click();
        cut.Find("[data-testid=decline-label]").Click();
        Assert.False(Labels.Exists);
        Assert.Empty(Registry.Removed);
        Assert.Empty(Registry.Updated);

        cut.Find("[data-testid=review-label]").Click();
        cut.Find("[data-testid=approve-label]").Click();

        cut.WaitForAssertion(() => Assert.True(Labels.Exists));
        Assert.Contains("available", cut.Find("[data-testid=label-status]").TextContent);
        Assert.Empty(Registry.Updated);
    }

    [Fact]
    public void a_deleted_label_is_detected_on_refresh_and_creation_errors_remain_retryable()
    {
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();
        Labels.Exists = false;
        cut.Find("[data-testid=check-label]").Click();
        cut.Find("[data-testid=review-label]").Click();
        Labels.FailCreation = true;

        cut.Find("[data-testid=approve-label]").Click();

        Assert.Contains("write permissions and retry", cut.Find("[data-testid=label-status]").TextContent);
        Assert.False(Labels.Exists);
        Assert.Empty(cut.FindAll("[data-testid=label-consent]"));

        Labels.FailCreation = false;
        cut.Find("[data-testid=check-label]").Click();
        cut.Find("[data-testid=review-label]").Click();
        cut.Find("[data-testid=approve-label]").Click();
        Assert.True(Labels.Exists);
    }

    [Fact]
    public void a_failed_lookup_shows_retry_not_a_creation_prompt()
    {
        Labels.FailLookup = true;
        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();

        Assert.Contains("permissions and retry", cut.Find("[data-testid=label-status]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=review-label]"));
        Assert.Empty(cut.FindAll("[data-testid=label-consent]"));

        Labels.FailLookup = false;
        cut.Find("[data-testid=check-label]").Click();
        Assert.Contains("available", cut.Find("[data-testid=label-status]").TextContent);
    }
}
