using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Api;

/// <summary>
/// Live server-sent events over the in-process event bus. The first message is <c>ready</c> (sent once the subscription is active);
/// every following message is named after the workflow event type and carries a <see cref="LiveEventView"/>. Delivery is at-least-once
/// and a slow client may miss events, so clients reload the affected projection on each message.
/// </summary>
internal static class EventStreamEndpoints
{
    private const string ReadyEventType = "ready";
    private const int BufferedEventsPerClient = 256;

    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup(string.Empty).WithTags("Events");

        group.MapGet("events/stream", ([FromServices] IRunEventBus bus, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(Stream(bus, null, cancellationToken)))
            .WithName("StreamEvents")
            .WithSummary("Live workflow events of all runs (text/event-stream).");

        group.MapGet("spec-runs/{id}/events/stream", async (
            string id,
            [FromServices] IRunQueries queries,
            [FromServices] IRunEventBus bus,
            CancellationToken cancellationToken) =>
            ApiIds.SpecRun(id) is { } runId && await queries.GetSpecRunAsync(runId, cancellationToken) is not null
                ? (IResult)TypedResults.ServerSentEvents(Stream(bus, runId.Value, cancellationToken))
                : ApiProblems.NotFound($"Spec run '{id}' does not exist."))
            .WithName("StreamSpecRunEvents")
            .WithSummary("Live workflow events of one spec run (text/event-stream).")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async IAsyncEnumerable<SseItem<LiveEventView>> Stream(
        IRunEventBus bus,
        string? specRunId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = Channel.CreateBounded<LiveEventView>(new BoundedChannelOptions(BufferedEventsPerClient)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        using IDisposable subscription = bus.Subscribe((envelope, _) =>
        {
            LiveEventView view = envelope.ToView();
            if (specRunId is null || view.SpecRunId == specRunId)
            {
                buffer.Writer.TryWrite(view);
            }

            return Task.CompletedTask;
        });

        yield return new SseItem<LiveEventView>(new LiveEventView(0, "Ready", specRunId, null, null, null, default), ReadyEventType);
        await foreach (LiveEventView view in buffer.Reader.ReadAllAsync(cancellationToken))
        {
            yield return new SseItem<LiveEventView>(view, view.Type) { EventId = view.MessageId.ToString() };
        }
    }
}
