using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Queries;

namespace WebDevLoop.Infrastructure.Tests.Queries;

public sealed class PersistentAgentLogStoreTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly StepRunId StepA = new("step-a");
    private static readonly StepRunId StepB = new("step-b");

    private readonly AgentLogDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static AgentLogEntry Entry(StepRunId step, string text, AgentLogKind kind = AgentLogKind.Assistant) => new(step, At, kind, text);

    private static async Task AppendAllAsync(PersistentAgentLogStore store, StepRunId step, params string[] texts)
    {
        foreach (string text in texts)
        {
            await store.AppendAsync(Entry(step, text), CancellationToken.None);
        }
    }

    [Fact]
    public async Task entries_are_numbered_per_step_in_arrival_order()
    {
        await using PersistentAgentLogStore store = _database.CreateStore();
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
        await using PersistentAgentLogStore store = _database.CreateStore();
        await AppendAllAsync(store, StepA, "1", "2", "3");

        IReadOnlyList<AgentLogView> newer = await store.ReadAsync(StepA, 1, CancellationToken.None);

        Assert.Equal(["2", "3"], newer.Select(view => view.Text));
        Assert.Empty(await store.ReadAsync(StepA, 3, CancellationToken.None));
    }

    [Fact]
    public async Task an_unknown_step_has_no_entries()
    {
        await using PersistentAgentLogStore store = _database.CreateStore();

        Assert.Empty(await store.ReadAsync(StepA, 0, CancellationToken.None));
    }

    [Fact]
    public async Task entries_survive_a_restart_and_numbering_continues()
    {
        await using (PersistentAgentLogStore first = _database.CreateStore())
        {
            await AppendAllAsync(first, StepA, "1", "2");
        }

        await using PersistentAgentLogStore restarted = _database.CreateStore();
        await AppendAllAsync(restarted, StepA, "3");

        IReadOnlyList<AgentLogView> all = await restarted.ReadAsync(StepA, 0, CancellationToken.None);

        Assert.Equal([(1, "1"), (2, "2"), (3, "3")], all.Select(view => (view.Sequence, view.Text)));
    }

    [Fact]
    public async Task entries_are_written_in_batches()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 3, FlushInterval = TimeSpan.FromMinutes(5) });

        await AppendAllAsync(store, StepA, "1", "2");
        Assert.Equal(0, await _database.CountStoredEntriesAsync());

        await AppendAllAsync(store, StepA, "3");
        Assert.Equal(3, await _database.CountStoredEntriesAsync());
    }

    [Fact]
    public async Task reading_includes_entries_still_waiting_for_their_batch()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 100, FlushInterval = TimeSpan.FromMinutes(5) });
        await AppendAllAsync(store, StepA, "1", "2");

        Assert.Equal(["1", "2"], (await store.ReadAsync(StepA, 0, CancellationToken.None)).Select(view => view.Text));
    }

    [Fact]
    public async Task buffered_entries_are_flushed_after_the_flush_interval()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 100, FlushInterval = TimeSpan.FromMilliseconds(30) });
        await AppendAllAsync(store, StepA, "1");

        await WaitUntilAsync(async () => await _database.CountStoredEntriesAsync() == 1);
    }

    [Fact]
    public async Task disposing_the_store_flushes_buffered_entries()
    {
        PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 100, FlushInterval = TimeSpan.FromMinutes(5) });
        await AppendAllAsync(store, StepA, "1");

        await store.DisposeAsync();

        Assert.Equal(1, await _database.CountStoredEntriesAsync());
    }

    [Fact]
    public async Task reads_are_paged_and_resume_from_the_last_sequence()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { PageSize = 2 });
        await AppendAllAsync(store, StepA, "1", "2", "3", "4", "5");

        IReadOnlyList<AgentLogView> first = await store.ReadAsync(StepA, 0, CancellationToken.None);
        IReadOnlyList<AgentLogView> second = await store.ReadAsync(StepA, first[^1].Sequence, CancellationToken.None);
        IReadOnlyList<AgentLogView> third = await store.ReadAsync(StepA, second[^1].Sequence, CancellationToken.None);

        Assert.Equal([["1", "2"], ["3", "4"], ["5"]], new[] { first, second, third }.Select(page => page.Select(view => view.Text).ToArray()));
    }

    [Fact]
    public async Task the_oldest_entries_of_a_step_are_deleted_beyond_the_retention_cap_but_sequences_keep_counting()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { MaxEntriesPerStep = 2, BatchSize = 1 });
        await AppendAllAsync(store, StepA, "1", "2", "3", "4");
        await AppendAllAsync(store, StepB, "b");

        IReadOnlyList<AgentLogView> kept = await store.ReadAsync(StepA, 0, CancellationToken.None);

        Assert.Equal([(3, "3"), (4, "4")], kept.Select(view => (view.Sequence, view.Text)));
        Assert.Equal(3, await _database.CountStoredEntriesAsync());
    }

    [Fact]
    public async Task retention_also_applies_within_a_single_large_batch()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { MaxEntriesPerStep = 2, BatchSize = 100 });
        await AppendAllAsync(store, StepA, "1", "2", "3", "4");

        Assert.Equal(["3", "4"], (await store.ReadAsync(StepA, 0, CancellationToken.None)).Select(view => view.Text));
        Assert.Equal(2, await _database.CountStoredEntriesAsync());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task subscribers_of_a_step_are_notified_when_a_full_batch_is_flushed()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 2, FlushInterval = TimeSpan.FromMinutes(5) });
        int notifications = 0;
        using IDisposable subscription = store.Subscribe(StepA, () => Interlocked.Increment(ref notifications));

        await store.AppendAsync(Entry(StepA, "1"), CancellationToken.None);
        Assert.Equal(0, notifications);
        await store.AppendAsync(Entry(StepA, "2"), CancellationToken.None);

        Assert.Equal(1, notifications);
        Assert.Equal(["1", "2"], (await store.ReadAsync(StepA, 0, CancellationToken.None)).Select(view => view.Text));
    }

    [Fact]
    public async Task subscribers_are_notified_when_the_flush_interval_writes_buffered_entries()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 100, FlushInterval = TimeSpan.FromMilliseconds(20) });
        var notified = new TaskCompletionSource();
        using IDisposable subscription = store.Subscribe(StepA, () => notified.TrySetResult());

        await store.AppendAsync(Entry(StepA, "1"), CancellationToken.None);

        await notified.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task only_subscribers_of_the_flushed_steps_are_notified()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 1 });
        List<string> notified = [];
        using IDisposable a = store.Subscribe(StepA, () => notified.Add("a"));
        using IDisposable b = store.Subscribe(StepB, () => notified.Add("b"));

        await store.AppendAsync(Entry(StepB, "1"), CancellationToken.None);

        Assert.Equal(["b"], notified);
    }

    [Fact]
    public async Task a_disposed_subscription_is_no_longer_notified()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 1 });
        int notifications = 0;
        IDisposable subscription = store.Subscribe(StepA, () => notifications++);
        subscription.Dispose();

        await store.AppendAsync(Entry(StepA, "1"), CancellationToken.None);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task a_failing_subscriber_neither_loses_entries_nor_blocks_the_others()
    {
        await using PersistentAgentLogStore store = _database.CreateStore(new AgentLogStoreOptions { BatchSize = 1 });
        int healthy = 0;
        using IDisposable failing = store.Subscribe(StepA, () => throw new InvalidOperationException("subscriber failed"));
        using IDisposable other = store.Subscribe(StepA, () => healthy++);

        await store.AppendAsync(Entry(StepA, "1"), CancellationToken.None);

        Assert.Equal(1, healthy);
        Assert.Equal(["1"], (await store.ReadAsync(StepA, 0, CancellationToken.None)).Select(view => view.Text));
    }

    [Fact]
    public async Task reading_without_buffered_entries_notifies_nobody()
    {
        await using PersistentAgentLogStore store = _database.CreateStore();
        int notifications = 0;
        using IDisposable subscription = store.Subscribe(StepA, () => notifications++);

        await store.ReadAsync(StepA, 0, CancellationToken.None);

        Assert.Equal(0, notifications);
    }
}
