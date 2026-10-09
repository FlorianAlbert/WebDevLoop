using System.Net;
using System.Text;
using System.Text.Json;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Issues;

internal sealed record IssuesApiRequest(HttpMethod Method, string PathAndQuery, string? Authorization, string Body)
{
    public string Path => PathAndQuery.Split('?')[0];

    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
}

/// <summary>Scripted stand-in for api.github.com's REST API; unscripted calls answer 404. Never touches the network.</summary>
internal sealed class FakeIssuesApiHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Pattern, Func<HttpResponseMessage> Response)> _routes = [];

    public List<IssuesApiRequest> Requests { get; } = [];

    public Exception? NetworkFailure { get; set; }

    /// <summary>Scripts a response; a pattern without '?' matches the path regardless of query string.</summary>
    public void Respond(HttpMethod method, string pattern, HttpStatusCode status, object? body = null, params (string Name, string Value)[] headers) =>
        _routes.Add((method, pattern, () => CreateResponse(status, body, headers)));

    public void Get(string pattern, object body, params (string Name, string Value)[] headers) =>
        Respond(HttpMethod.Get, pattern, HttpStatusCode.OK, body, headers);

    public IEnumerable<IssuesApiRequest> RequestsTo(HttpMethod method, string path) =>
        Requests.Where(request => request.Method == method && request.Path == path);

    public static object Issue(int number, long id, string title = "Title", string body = "", string state = "open", string repo = "widgets") => new
    {
        number,
        id,
        node_id = $"I_{id}",
        title,
        body,
        state,
        repository_url = $"https://api.github.com/repos/acme/{repo}",
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        string pathAndQuery = request.RequestUri!.PathAndQuery;
        Requests.Add(new IssuesApiRequest(request.Method, pathAndQuery, request.Headers.Authorization?.ToString(), body));

        if (NetworkFailure is not null)
        {
            throw NetworkFailure;
        }

        var candidates = _routes.Where(route => route.Method == request.Method).ToList();
        var match = candidates.FirstOrDefault(route => route.Pattern == pathAndQuery);
        if (match.Response is null)
        {
            match = candidates.FirstOrDefault(route => !route.Pattern.Contains('?') && route.Pattern == request.RequestUri.AbsolutePath);
        }

        return match.Response?.Invoke() ?? CreateResponse(HttpStatusCode.NotFound, new { message = "Not Found" }, []);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode status, object? body, (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        foreach ((string name, string value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}
