using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Web.Tests.Api;

public sealed class EventStreamEndpointTests
{
    private static readonly RunId Run = new("run-1");
    private static readonly RunId OtherRun = new("run-2");

    private static async Task<SseReader> OpenAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        return new SseReader(response, await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task the_run_stream_signals_readiness_then_delivers_only_events_of_that_run()
    {
        await using var factory = new ApiFactory();
        factory.Runs.SpecRuns.Add(ApiData.SpecRun("run-1"));
        using HttpClient client = factory.CreateClient();
        using SseReader stream = await OpenAsync(client, "/api/spec-runs/run-1/events/stream");

        SseMessage ready = await stream.ReadAsync();
        await factory.EventBus.PublishAsync(new EventEnvelope(1, new SpecRunStatusChanged(OtherRun, 1, SpecRunStatus.Queued, SpecRunStatus.Preparing, ApiData.Now)), CancellationToken.None);
        await factory.EventBus.PublishAsync(new EventEnvelope(2, new SpecRunStatusChanged(Run, 1, SpecRunStatus.Queued, SpecRunStatus.Preparing, ApiData.Now)), CancellationToken.None);
        SseMessage message = await stream.ReadAsync();

        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal("text/event-stream", stream.ContentType);
        Assert.Equal("ready", ready.EventType);
        Assert.Equal(("SpecRunStatusChanged", "2"), (message.EventType, message.Id));
        JsonElement data = JsonDocument.Parse(message.Data).RootElement;
        Assert.Equal(("run-1", "Preparing", "SpecRunStatusChanged"), (data.GetProperty("specRunId").GetString(), data.GetProperty("status").GetString(), data.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task the_global_stream_delivers_events_of_every_run()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();
        using SseReader stream = await OpenAsync(client, "/api/events/stream");

        await stream.ReadAsync();
        await factory.EventBus.PublishAsync(new EventEnvelope(1, new TicketRunStatusChanged(OtherRun, new TicketRunId("t-9"), TicketRunStatus.Ready, TicketRunStatus.Implementing, ApiData.Now)), CancellationToken.None);
        SseMessage message = await stream.ReadAsync();

        JsonElement data = JsonDocument.Parse(message.Data).RootElement;
        Assert.Equal(("TicketRunStatusChanged", "run-2", "t-9", "Implementing"), (message.EventType, data.GetProperty("specRunId").GetString(), data.GetProperty("ticketRunId").GetString(), data.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task streaming_an_unknown_run_returns_404()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/spec-runs/missing/events/stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task closing_the_stream_unsubscribes_from_the_event_bus()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();
        SseReader stream = await OpenAsync(client, "/api/events/stream");
        await stream.ReadAsync();
        Assert.Equal(1, factory.EventBus.SubscriberCount);

        stream.Dispose();

        await WaitUntilAsync(() => factory.EventBus.SubscriberCount == 0);
        Assert.Equal(0, factory.EventBus.SubscriberCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}

internal sealed record SseMessage(string? EventType, string? Id, string Data);

internal sealed class SseReader(HttpResponseMessage response, Stream body) : IDisposable
{
    private readonly StreamReader _reader = new(body);

    public HttpStatusCode StatusCode => response.StatusCode;

    public string? ContentType => response.Content.Headers.ContentType?.MediaType;

    public async Task<SseMessage> ReadAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? eventType = null;
        string? id = null;
        var data = new List<string>();
        while (await _reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Count > 0 || eventType is not null)
                {
                    return new SseMessage(eventType, id, string.Join('\n', data));
                }

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventType = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                id = line["id:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Add(line["data:".Length..].TrimStart());
            }
        }

        throw new InvalidOperationException("The event stream ended before a message arrived.");
    }

    public void Dispose()
    {
        _reader.Dispose();
        response.Dispose();
    }
}
