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

    [Theory]
    [InlineData("low", true, true)]
    [InlineData("high", true, true)]
    [InlineData("High", true, false)]
    [InlineData("medium", true, false)]
    [InlineData("", true, false)]
    [InlineData("high", false, true)]
    [InlineData("custom-effort", false, true)]
    [InlineData("", false, true)]
    public void configured_reasoning_effort_is_unconstrained_without_reasoning_support(
        string effort, bool hasReasoningSupport, bool expected)
    {
        ModelCatalogEntry model = new("test-model", "Test Model", hasReasoningSupport ? ["low", "high"] : []);

        Assert.Equal(expected, model.IsReasoningEffortValid(effort));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void reasoning_support_requires_catalog_effort_choices(bool hasReasoningSupport)
    {
        ModelCatalogEntry model = new("test-model", "Test Model", hasReasoningSupport ? ["low", "high"] : []);

        Assert.Equal(hasReasoningSupport, model.SupportsReasoning);
    }
}
