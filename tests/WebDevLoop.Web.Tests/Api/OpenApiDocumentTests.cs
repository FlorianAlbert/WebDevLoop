using System.Net;
using System.Text.Json;

namespace WebDevLoop.Web.Tests.Api;

public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task the_openapi_document_describes_the_api_routes()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/openapi/v1.json");
        JsonElement document = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string[] paths = document.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();
        Assert.Contains("/api/health", paths);
        Assert.Contains("/api/repos", paths);
        Assert.Contains("/api/repos/{repoId}/spec-runs", paths);
        Assert.Contains("/api/steps/{id}/logs", paths);
        Assert.Contains("/api/spec-runs/{id}/merge-status", paths);
        Assert.Contains("/api/spec-runs/{id}/retry", paths);
        Assert.Contains("/api/ticket-runs/{id}/skip", paths);
    }
}
