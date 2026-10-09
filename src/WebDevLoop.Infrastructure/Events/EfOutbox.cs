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
    /// <summary>Failed deliveries after which a message is dead-lettered instead of being retried forever.</summary>
    public const int MaxDeliveryAttempts = 10;

    public void Append(WorkflowEvent workflowEvent)
    {
        ArgumentNullException.ThrowIfNull(workflowEvent);

        (string type, string payloadJson) = WorkflowEventSerializer.Serialize(workflowEvent);
        messages.Add(OutboxMessage.Create(type, payloadJson, clock.UtcNow));
    }

    /// <summary>
    /// Rows that cannot be read back are dead-lettered so they never block valid messages. Because dead-lettered rows
    /// free their slots, the read repeats until it finds a readable message or the pending set is exhausted.
    /// </summary>
    public async Task<IReadOnlyList<EventEnvelope>> ReadPendingAsync(int maxCount, CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<OutboxMessage> pending = await messages.ListPendingAsync(maxCount, cancellationToken);
            List<EventEnvelope> envelopes = new(pending.Count);
            bool deadLettered = false;

            foreach (OutboxMessage message in pending)
            {
                try
                {
                    envelopes.Add(new EventEnvelope(message.Id, WorkflowEventSerializer.Deserialize(message.Type, message.PayloadJson)));
                }
                catch (JsonException exception)
                {
                    message.RecordFailure($"Unreadable outbox message: {exception.Message}");
                    message.DeadLetter(clock.UtcNow, message.LastError!);
                    deadLettered = true;
                }
            }

            if (deadLettered)
            {
                await SaveAsync(cancellationToken);
            }

            if (envelopes.Count > 0 || !deadLettered)
            {
                return envelopes;
            }
        }
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
        if (message.Attempts >= MaxDeliveryAttempts)
        {
            message.DeadLetter(clock.UtcNow, error);
        }

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
