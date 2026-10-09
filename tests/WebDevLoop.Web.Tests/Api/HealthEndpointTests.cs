using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Api;

public sealed class HealthEndpointTests
{
    private static readonly PrerequisiteCheck Failed = new("Git CLI", PrerequisiteStatus.Failed, "git not found", "Install git.");
    private static readonly PrerequisiteCheck Warned = new("gh stack", PrerequisiteStatus.Warning, "gh stack missing", "Install gh-stack.");

    [Fact]
    public async Task health_is_healthy_and_operational_when_all_prerequisites_pass()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/health");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Operational", body.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task health_is_unhealthy_in_diagnostic_only_mode()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync(Failed);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/health");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        Assert.Equal("DiagnosticOnly", body.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task health_is_unhealthy_until_prerequisites_were_evaluated()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task prerequisites_list_every_check_with_remediation_even_in_diagnostic_mode()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync(Failed, Warned);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/prerequisites");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DiagnosticOnly", body.GetProperty("mode").GetString());
        JsonElement[] checks = body.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(["Git CLI", "gh stack"], checks.Select(check => check.GetProperty("name").GetString()));
        Assert.Equal(["Failed", "Warning"], checks.Select(check => check.GetProperty("status").GetString()));
        Assert.Equal("Install git.", checks[0].GetProperty("remediation").GetString());
        Assert.True(body.TryGetProperty("evaluatedAt", out _));
    }

    [Fact]
    public async Task prerequisites_are_empty_before_the_first_evaluation()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement body = await client.GetJsonAsync("/api/prerequisites");

        Assert.Equal("DiagnosticOnly", body.GetProperty("mode").GetString());
        Assert.Equal(0, body.GetProperty("checks").GetArrayLength());
    }
}
