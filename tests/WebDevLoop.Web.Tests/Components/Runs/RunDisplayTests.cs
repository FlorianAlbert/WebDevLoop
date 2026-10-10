using WebDevLoop.Core.Agents;
using WebDevLoop.Web.Components.Runs;

namespace WebDevLoop.Web.Tests.Components.Runs;

public sealed class RunDisplayTests
{
    [Theory]
    [InlineData(AgentCapability.CreateLocalCommit, "Create local commit")]
    [InlineData(AgentCapability.RunShellCommands, "Run shell commands")]
    [InlineData(AgentCapability.ReportResult, "Report result")]
    public void Capabilities_are_humanized(AgentCapability capability, string expected) =>
        Assert.Equal(expected, RunDisplay.Capability(capability));

    [Fact]
    public void Token_access_is_described_in_words() =>
        Assert.Equal("Read-only, only if required", RunDisplay.TokenAccess(GitHubTokenAccess.ReadOnlyIfRequired));

    [Fact]
    public void Long_hashes_are_shortened_and_short_values_are_kept()
    {
        Assert.Equal("0123abcd", RunDisplay.ShortHash("0123abcd4567ef890123abcd4567ef890123abcd"));
        Assert.Equal("copilot-1", RunDisplay.ShortHash("copilot-1"));
        Assert.Equal("–", RunDisplay.ShortHash(null));
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(1800, "30 min")]
    [InlineData(5400, "1 h 30 min")]
    public void Durations_are_compact(int seconds, string expected) =>
        Assert.Equal(expected, RunDisplay.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Timeout_is_the_span_between_start_and_deadline()
    {
        DateTimeOffset start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal("45 min", RunDisplay.Timeout(start, start.AddMinutes(45)));
        Assert.Null(RunDisplay.Timeout(null, start));
    }
}
