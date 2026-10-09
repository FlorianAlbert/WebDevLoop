using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Pulls;

internal sealed record RecordedCall(HttpMethod Method, string PathAndQuery, string? Authorization, string Body)
{
    public JsonNode Json => JsonNode.Parse(Body)!;
}

/// <summary>Scripted api.github.com: answers by "METHOD /path?query" with queued responses (last one repeats); never touches the network.</summary>
internal sealed class StubGitHubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Json)>> _responses = [];

    public List<RecordedCall> Calls { get; } = [];

    public StubGitHubHandler Respond(HttpMethod method, string pathAndQuery, HttpStatusCode status, object json)
    {
        string key = $"{method} {pathAndQuery}";
        if (!_responses.TryGetValue(key, out var queue))
        {
            _responses[key] = queue = new();
        }

        queue.Enqueue((status, JsonSerializer.Serialize(json)));
        return this;
    }

    public IEnumerable<RecordedCall> CallsTo(HttpMethod method, string pathAndQuery) =>
        Calls.Where(call => call.Method == method && call.PathAndQuery == pathAndQuery);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        string pathAndQuery = request.RequestUri!.PathAndQuery;
        Calls.Add(new RecordedCall(request.Method, pathAndQuery, request.Headers.Authorization?.ToString(), body));

        if (!_responses.TryGetValue($"{request.Method} {pathAndQuery}", out var queue))
        {
            return Reply(HttpStatusCode.NotFound, JsonSerializer.Serialize(new { message = "unexpected request" }));
        }

        var (status, json) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return Reply(status, json);
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
