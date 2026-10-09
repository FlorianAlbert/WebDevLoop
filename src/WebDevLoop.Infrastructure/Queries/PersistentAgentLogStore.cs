using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Persistence;

namespace WebDevLoop.Infrastructure.Queries;

/// <summary>
/// Agent log in SQLite so step logs survive a restart. Appends are buffered and written in batches (when
/// <see cref="AgentLogStoreOptions.BatchSize"/> entries are pending, after <see cref="AgentLogStoreOptions.FlushInterval"/>,
/// before every read and on disposal); a read therefore always sees everything appended before it, which is what feeds the live view.
/// Each flush also deletes entries beyond the per-step retention cap.
/// </summary>
public sealed class PersistentAgentLogStore(AgentLogStoreOptions options, IServiceScopeFactory scopes) : IAgentLogSink, IAgentLogReader, IAgentLogNotifications, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AgentLogSubscriptions _subscriptions = new();
    private readonly List<AgentLogRecord> _pending = [];
    private readonly Dictionary<StepRunId, int> _lastSequences = [];
    private bool _flushScheduled;

    public async ValueTask AppendAsync(AgentLogEntry entry, CancellationToken cancellationToken)
    {
        IReadOnlyList<StepRunId> flushed = [];
        await _gate.WaitAsync(cancellationToken);
        try
        {
            int sequence = await NextSequenceAsync(entry.StepRunId, cancellationToken);
            _pending.Add(new AgentLogRecord { StepRunId = entry.StepRunId, Sequence = sequence, At = entry.At, Kind = entry.Kind, Text = entry.Text });

            if (_pending.Count >= options.BatchSize)
            {
                flushed = await FlushPendingAsync(cancellationToken);
            }
            else if (!_flushScheduled)
            {
                _flushScheduled = true;
                _ = FlushAfterIntervalAsync();
            }
        }
        finally
        {
            _gate.Release();
        }

        _subscriptions.Notify(flushed);
    }

    public async Task<IReadOnlyList<AgentLogView>> ReadAsync(StepRunId stepRunId, int afterSequence, CancellationToken cancellationToken)
    {
        await FlushAsync(cancellationToken);

        using IServiceScope scope = scopes.CreateScope();
        WebDevLoopDbContext context = scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>();
        return await context.AgentLogEntries
            .AsNoTracking()
            .Where(entry => entry.StepRunId == stepRunId && entry.Sequence > afterSequence)
            .OrderBy(entry => entry.Sequence)
            .Take(options.PageSize)
            .Select(entry => new AgentLogView(entry.Sequence, entry.At, entry.Kind, entry.Text))
            .ToListAsync(cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StepRunId> flushed;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            flushed = await FlushPendingAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        _subscriptions.Notify(flushed);
    }

    public IDisposable Subscribe(StepRunId stepRunId, Action onEntriesAvailable) => _subscriptions.Subscribe(stepRunId, onEntriesAvailable);

    public async ValueTask DisposeAsync() => await FlushAsync(CancellationToken.None);

    private async Task<int> NextSequenceAsync(StepRunId stepRunId, CancellationToken cancellationToken)
    {
        if (!_lastSequences.TryGetValue(stepRunId, out int last))
        {
            using IServiceScope scope = scopes.CreateScope();
            WebDevLoopDbContext context = scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>();
            last = await context.AgentLogEntries
                .Where(entry => entry.StepRunId == stepRunId)
                .MaxAsync(entry => (int?)entry.Sequence, cancellationToken) ?? 0;
        }

        _lastSequences[stepRunId] = ++last;
        return last;
    }

    /// <returns>The steps that received entries; notified by the caller once the gate is released.</returns>
    private async Task<IReadOnlyList<StepRunId>> FlushPendingAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        using IServiceScope scope = scopes.CreateScope();
        WebDevLoopDbContext context = scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>();
        context.AgentLogEntries.AddRange(_pending);
        await context.SaveChangesAsync(cancellationToken);

        // Cleared before the retention delete so a failing delete can never make a retry re-insert the same rows.
        StepRunId[] touchedSteps = _pending.Select(entry => entry.StepRunId).Distinct().ToArray();
        _pending.Clear();

        foreach (StepRunId stepRunId in touchedSteps)
        {
            int oldestRetained = _lastSequences[stepRunId] - options.MaxEntriesPerStep;
            await context.AgentLogEntries
                .Where(entry => entry.StepRunId == stepRunId && entry.Sequence <= oldestRetained)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return touchedSteps;
    }

    private async Task FlushAfterIntervalAsync()
    {
        try
        {
            await Task.Delay(options.FlushInterval);
            IReadOnlyList<StepRunId> flushed;
            await _gate.WaitAsync();
            try
            {
                _flushScheduled = false;
                flushed = await FlushPendingAsync(CancellationToken.None);
            }
            finally
            {
                _gate.Release();
            }

            _subscriptions.Notify(flushed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Entries stay pending; the next append, read or disposal retries and surfaces a persistent failure.
        }
    }
}
