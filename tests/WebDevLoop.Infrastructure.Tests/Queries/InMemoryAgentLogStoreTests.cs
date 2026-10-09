using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Queries;

namespace WebDevLoop.Infrastructure.Tests.Queries;

public sealed class InMemoryAgentLogStoreTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly StepRunId StepA = new("step-a");
    private static readonly StepRunId StepB = new("step-b");

    private static AgentLogEntry Entry(StepRunId step, string text, AgentLogKind kind = AgentLogKind.Assistant) => new(step, At, kind, text);

    [Fact]
    public async Task entries_are_numbered_per_step_in_arrival_order()
    {
        var store = new InMemoryAgentLogStore(new AgentLogStoreOptions());
        await store.AppendAsync(Entry(StepA, "a1"), CancellationToken.None);
        await store.AppendAsync(Entry(StepB, "b1", AgentLogKind.Error), CancellationToken.None);
        await store.AppendAsync(Entry(StepA, "a2", AgentLogKind.ToolStarted), CancellationToken.None);

        IReadOnlyList<AgentLogView> a = await store.ReadAsync(StepA, 0, CancellationToken.None);
        IReadOnlyList<AgentLogView> b = await store.ReadAsync(StepB, 0, CancellationToken.None);

        Assert.Equal([(1, "a1", AgentLogKind.Assistant), (2, "a2", AgentLogKind.ToolStarted)], a.Select(view => (view.Sequence, view.Text, view.Kind)));
        Assert.Equal([(1, "b1", AgentLogKind.Error)], b.Select(view => (view.Sequence, view.Text, view.Kind)));
        Assert.Equal(At, a[0].At);
    }

    [Fact]
    public async Task reading_after_a_sequence_returns_only_newer_entries()
    {
        var store = new InMemoryAgentLogStore(new AgentLogStoreOptions());
        foreach (string text in new[] { "1", "2", "3" })
        {
            await store.AppendAsync(Entry(StepA, text), CancellationToken.None);
        }

        IReadOnlyList<AgentLogView> newer = await store.ReadAsync(StepA, 1, CancellationToken.None);

        Assert.Equal(["2", "3"], newer.Select(view => view.Text));
        Assert.Empty(await store.ReadAsync(StepA, 3, CancellationToken.None));
    }

    [Fact]
    public async Task an_unknown_step_has_no_entries()
    {
        var store = new InMemoryAgentLogStore(new AgentLogStoreOptions());

        Assert.Empty(await store.ReadAsync(StepA, 0, CancellationToken.None));
    }

    [Fact]
    public async Task the_oldest_entries_of_a_step_are_dropped_beyond_the_per_step_limit_but_sequences_keep_counting()
    {
        var store = new InMemoryAgentLogStore(new AgentLogStoreOptions(MaxEntriesPerStep: 2));
        foreach (string text in new[] { "1", "2", "3", "4" })
        {
            await store.AppendAsync(Entry(StepA, text), CancellationToken.None);
        }

        IReadOnlyList<AgentLogView> kept = await store.ReadAsync(StepA, 0, CancellationToken.None);

        Assert.Equal([(3, "3"), (4, "4")], kept.Select(view => (view.Sequence, view.Text)));
    }

    [Fact]
    public async Task the_least_recently_written_step_is_evicted_beyond_the_step_limit()
    {
        var store = new InMemoryAgentLogStore(new AgentLogStoreOptions(MaxSteps: 2));
        await store.AppendAsync(Entry(StepA, "a"), CancellationToken.None);
        await store.AppendAsync(Entry(StepB, "b"), CancellationToken.None);
        await store.AppendAsync(Entry(StepA, "a again"), CancellationToken.None);
        await store.AppendAsync(Entry(new StepRunId("step-c"), "c"), CancellationToken.None);

        Assert.Empty(await store.ReadAsync(StepB, 0, CancellationToken.None));
        Assert.Equal(2, (await store.ReadAsync(StepA, 0, CancellationToken.None)).Count);
        Assert.Single(await store.ReadAsync(new StepRunId("step-c"), 0, CancellationToken.None));
    }
}
