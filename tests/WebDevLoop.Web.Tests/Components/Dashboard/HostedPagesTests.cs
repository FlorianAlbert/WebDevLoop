using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.DependencyInjection;
using WebDevLoop.Web.Tests.Api;

namespace WebDevLoop.Web.Tests.Components.Dashboard;

/// <summary>The real host (Program.cs + the UI registration) serving the pages interactively, with every application service faked.</summary>
public sealed class HostedPagesTests : IAsyncLifetime
{
    private ApiFactory _factory = default!;
    private HttpClient _client = default!;

    public async ValueTask InitializeAsync()
    {
        _factory = await new ApiFactory().EvaluateAsync();
        _factory.Repositories.Repositories.Add(ApiData.Repository(1, "widgets"));
        _factory.Runs.SpecRuns.Add(ApiData.SpecRun("run-1", 1, SpecRunStatus.AwaitingMerge));
        _factory.Settings.EffectiveResult = CommandResult<EffectiveSettingsView>.NotFound("none");
        _client = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddWebDevLoopDashboardUi())).CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("/", "Dashboard")]
    [InlineData("/repositories", "Repositories")]
    [InlineData("/queue", "Select a repository")]
    public async Task page_is_served_with_the_interactive_server_render_mode(string path, string expectedText)
    {
        HttpResponseMessage response = await _client.GetAsync(path, Xunit.TestContext.Current.CancellationToken);
        string html = await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, html);
        Assert.Contains("\"type\":\"server\"", html);
        Assert.Contains("acme/widgets", html);
    }

    [Fact]
    public async Task home_page_prerenders_the_awaiting_merge_alert_and_navigation()
    {
        string html = await _client.GetStringAsync("/", Xunit.TestContext.Current.CancellationToken);

        Assert.Contains("data-alert=\"AwaitingMerge\"", html);
        Assert.Contains("href=\"/settings\"", html);
        Assert.Contains("href=\"/health\"", html);
    }
}
