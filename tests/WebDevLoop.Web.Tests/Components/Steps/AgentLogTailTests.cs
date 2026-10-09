using Bunit;
using WebDevLoop.Core.Agents;
using WebDevLoop.Web.Components.Steps;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Steps;

public sealed class AgentLogTailTests
{
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(20);

    [Fact]
    public void Existing_entries_are_shown_with_their_kind()
    {
        using var harness = new RunDetailHarness();
        harness.Logs.Append("Reading files");
        harness.Logs.Append("npm test failed", AgentLogKind.Error);

        var cut = harness.Render<AgentLogTail>(p => p.Add(c => c.StepRunId, "s1").Add(c => c.PollInterval, TimeSpan.Zero));

        var entries = cut.FindAll("[data-testid=log-entry]");
        Assert.Equal(2, entries.Count);
        Assert.Contains("Reading files", entries[0].TextContent);
        Assert.Equal("Error", entries[1].GetAttribute("data-kind"));
    }

    [Fact]
    public void Empty_log_shows_a_hint()
    {
        using var harness = new RunDetailHarness();

        var cut = harness.Render<AgentLogTail>(p => p.Add(c => c.StepRunId, "s1").Add(c => c.PollInterval, TimeSpan.Zero));

        Assert.NotNull(cut.Find("[data-testid=log-empty]"));
    }

    [Fact]
    public void New_entries_are_tailed_while_following_using_the_last_sequence()
    {
        using var harness = new RunDetailHarness();
        harness.Logs.Append("first");
        var cut = harness.Render<AgentLogTail>(p => p.Add(c => c.StepRunId, "s1").Add(c => c.Follow, true).Add(c => c.PollInterval, FastPoll));

        harness.Logs.Append("second");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=log-entry]").Count));
        Assert.Contains(1, harness.Logs.AfterSequences);
        Assert.Equal(1, harness.Logs.AfterSequences.Count(after => after == 0));
    }

    [Fact]
    public async Task Log_is_not_polled_when_not_following()
    {
        using var harness = new RunDetailHarness();
        harness.Logs.Append("first");
        harness.Render<AgentLogTail>(p => p.Add(c => c.StepRunId, "s1").Add(c => c.Follow, false).Add(c => c.PollInterval, FastPoll));

        await Task.Delay(150, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.Logs.ReadCalls);
    }

    [Fact]
    public void Only_the_newest_entries_are_shown_with_a_hidden_count()
    {
        using var harness = new RunDetailHarness();
        for (int i = 1; i <= 5; i++)
        {
            harness.Logs.Append($"line {i}");
        }

        var cut = harness.Render<AgentLogTail>(p => p.Add(c => c.StepRunId, "s1").Add(c => c.MaxEntries, 2).Add(c => c.PollInterval, TimeSpan.Zero));

        var entries = cut.FindAll("[data-testid=log-entry]");
        Assert.Equal(2, entries.Count);
        Assert.Contains("line 5", entries[1].TextContent);
        Assert.Contains("3", cut.Find("[data-testid=log-hidden]").TextContent);
    }

    [Fact]
    public void Entries_written_just_before_the_step_ends_are_read_once_more()
    {
        using var harness = new RunDetailHarness();
        harness.Logs.Append("first");
        var cut = harness.Render<AgentLogTail>(p => p.Add(c => c.StepRunId, "s1").Add(c => c.Follow, true).Add(c => c.PollInterval, TimeSpan.Zero));

        harness.Logs.Append("last words");
        cut.Render(p => p.Add(c => c.StepRunId, "s1").Add(c => c.Follow, false).Add(c => c.PollInterval, TimeSpan.Zero));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=log-entry]").Count));
    }
}
