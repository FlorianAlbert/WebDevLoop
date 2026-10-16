using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class ScriptedModelCatalogTests
{
    [Fact]
    public async Task successful_refresh_marks_even_an_empty_catalog_as_fetched()
    {
        IModelCatalog catalog = new ScriptedModelCatalog();

        Assert.False(catalog.HasFetched);
        Assert.Empty(catalog.Models);

        ModelCatalogRefreshResult result = await catalog.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.True(catalog.HasFetched);
        Assert.Empty(catalog.Models);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task failed_refresh_reports_the_scripted_error_and_preserves_catalog_state(bool hasFetched)
    {
        ScriptedModelCatalog scripted = new()
        {
            Models = [new ModelCatalogEntry("claude-sonnet-5.5", "Claude Sonnet 5.5", ["medium", "high"])],
            HasFetched = hasFetched,
            RefreshResult = new ModelCatalogRefreshResult("Copilot is unavailable"),
        };
        IModelCatalog catalog = scripted;

        ModelCatalogRefreshResult result = await catalog.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("Copilot is unavailable", result.Error);
        Assert.Equal(hasFetched, catalog.HasFetched);
        ModelCatalogEntry model = Assert.Single(catalog.Models);
        Assert.Equal("claude-sonnet-5.5", model.Id);
        Assert.Equal("Claude Sonnet 5.5", model.DisplayName);
        Assert.Equal(["medium", "high"], model.SupportedReasoningEfforts);
    }
}
