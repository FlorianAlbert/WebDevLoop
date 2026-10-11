using WebDevLoop.Core.Agents;

namespace WebDevLoop.Core.Tests.Agents;

public sealed class ModelCatalogEntryTests
{
    [Theory]
    [InlineData("low", true, true)]
    [InlineData("high", true, true)]
    [InlineData("High", true, false)]
    [InlineData("medium", true, false)]
    [InlineData("", true, false)]
    [InlineData("high", false, false)]
    [InlineData("", false, false)]
    public void reasoning_effort_support_requires_an_exact_catalog_match(string effort, bool hasReasoningSupport, bool expected)
    {
        ModelCatalogEntry model = new("test-model", "Test Model", hasReasoningSupport ? ["low", "high"] : []);

        Assert.Equal(expected, model.SupportsReasoningEffort(effort));
    }
}
