using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Infrastructure.Tests.Persistence;

namespace WebDevLoop.Infrastructure.Tests.Events;

public sealed class OutboxRetentionTests : IDisposable
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly PersistenceHarness _harness = new();
    private readonly FixedClock _clock = new(TestData.Now);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Dispatched_messages_older_than_the_retention_are_purged_and_newer_ones_kept()
    {
        long old = await AppendDispatchedAsync("run-old");
        _clock.Advance(Retention);
        long recent = await AppendDispatchedAsync("run-recent");
        _clock.Advance(TimeSpan.FromMinutes(1));

        int purged = await PurgeAsync();

        Assert.Equal(1, purged);
        using PersistenceScope verify = _harness.OpenScope();
        Assert.Null(await verify.Outbox.GetAsync(old, CancellationToken.None));
        Assert.NotNull(await verify.Outbox.GetAsync(recent, CancellationToken.None));
    }

    [Fact]
    public async Task Pending_and_dead_lettered_messages_are_never_purged()
    {
        long pending = await AppendAsync("run-pending");
        long deadLettered = await AppendAsync("run-dead");
        using (PersistenceScope scope = _harness.OpenScope())
        {
            var outbox = new EfOutbox(scope.Outbox, scope.UnitOfWork, _clock);
            for (int attempt = 0; attempt < EfOutbox.MaxDeliveryAttempts; attempt++)
            {
                await outbox.RecordFailureAsync(deadLettered, "bus down", CancellationToken.None);
            }
        }

        _clock.Advance(Retention * 2);

        Assert.Equal(0, await PurgeAsync());
        using PersistenceScope verify = _harness.OpenScope();
        Assert.NotNull(await verify.Outbox.GetAsync(pending, CancellationToken.None));
        Assert.NotNull(await verify.Outbox.GetAsync(deadLettered, CancellationToken.None));
    }

    private async Task<int> PurgeAsync()
    {
        using PersistenceScope scope = _harness.OpenScope();
        return await new OutboxRetention(scope.Context, _clock).PurgeDispatchedAsync(Retention, CancellationToken.None);
    }

    private async Task<long> AppendDispatchedAsync(string runId)
    {
        long id = await AppendAsync(runId);
        using PersistenceScope scope = _harness.OpenScope();
        await new EfOutbox(scope.Outbox, scope.UnitOfWork, _clock).MarkDispatchedAsync(id, CancellationToken.None);
        return id;
    }

    private async Task<long> AppendAsync(string runId)
    {
        using PersistenceScope scope = _harness.OpenScope();
        new EfOutbox(scope.Outbox, scope.UnitOfWork, _clock).Append(new FrontierReconciliationRequested(new RunId(runId), _clock.UtcNow));
        await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        return scope.Context.OutboxMessages.Max(message => message.Id);
    }
}
