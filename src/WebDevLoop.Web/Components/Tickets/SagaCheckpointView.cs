using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Tickets;

public sealed record SagaCheckpointView(
    IntegrationSagaCheckpoint Checkpoint,
    string StackBranch,
    int? PullRequestNumber,
    string? LastError,
    DateTimeOffset UpdatedAt)
{
    public static SagaCheckpointView From(IntegrationSaga saga) => new(
        saga.Checkpoint, saga.StackBranchName.Value, saga.PullRequestNumber?.Value, saga.LastError, saga.UpdatedAt);

    /// <summary>Ticket statuses from which an integration saga may exist.</summary>
    private static bool MayHaveSaga(TicketRunStatus status) =>
        status is TicketRunStatus.Integrating or TicketRunStatus.Integrated or TicketRunStatus.NeedsAttention;

    public static async Task<IReadOnlyDictionary<string, SagaCheckpointView>> LoadAsync(
        IIntegrationSagaRepository sagas,
        IEnumerable<TicketRunView> tickets,
        CancellationToken cancellationToken)
    {
        Dictionary<string, SagaCheckpointView> result = [];
        foreach (TicketRunView ticket in tickets.Where(ticket => MayHaveSaga(ticket.Status)))
        {
            if (await sagas.FindLatestForTicketAsync(new TicketRunId(ticket.Id), cancellationToken) is { } saga)
            {
                result[ticket.Id] = From(saga);
            }
        }

        return result;
    }
}
