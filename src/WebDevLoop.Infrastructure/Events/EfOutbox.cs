using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Persistence.Repositories;

namespace WebDevLoop.Infrastructure.Events;

/// <summary>
/// <see cref="IOutbox"/> over the row-level <see cref="IOutboxMessageRepository"/>. Appended rows join the scope's pending
/// changes, so they are saved by the same <see cref="IUnitOfWork.SaveChangesAsync"/> as the state change that raised them.
/// </summary>
public sealed class EfOutbox(IOutboxMessageRepository messages, IUnitOfWork unitOfWork, IClock clock) : IOutbox
{
    public void Append(WorkflowEvent workflowEvent)
    {
        ArgumentNullException.ThrowIfNull(workflowEvent);

        (string type, string payloadJson) = WorkflowEventSerializer.Serialize(workflowEvent);
        messages.Add(OutboxMessage.Create(type, payloadJson, clock.UtcNow));
    }

    /// <summary>Rows that cannot be read back are skipped and flagged with a recorded failure so they never block valid messages.</summary>
    public async Task<IReadOnlyList<EventEnvelope>> ReadPendingAsync(int maxCount, CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxMessage> pending = await messages.ListPendingAsync(maxCount, cancellationToken);
        List<EventEnvelope> envelopes = new(pending.Count);
        bool flaggedUnreadable = false;

        foreach (OutboxMessage message in pending)
        {
            try
            {
                envelopes.Add(new EventEnvelope(message.Id, WorkflowEventSerializer.Deserialize(message.Type, message.PayloadJson)));
            }
            catch (JsonException exception)
            {
                message.RecordFailure($"Unreadable outbox message: {exception.Message}");
                flaggedUnreadable = true;
            }
        }

        if (flaggedUnreadable)
        {
            await SaveAsync(cancellationToken);
        }

        return envelopes;
    }

    public async Task MarkDispatchedAsync(long messageId, CancellationToken cancellationToken)
    {
        OutboxMessage? message = await messages.GetAsync(messageId, cancellationToken);
        if (message is null || !message.IsPending)
        {
            return;
        }

        message.MarkDispatched(clock.UtcNow);
        await SaveAsync(cancellationToken);
    }

    public async Task RecordFailureAsync(long messageId, string error, CancellationToken cancellationToken)
    {
        OutboxMessage? message = await messages.GetAsync(messageId, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.RecordFailure(error);
        await SaveAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        SaveOutcome outcome = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            throw new InvalidOperationException($"Saving outbox changes failed: {outcome}.");
        }
    }
}
