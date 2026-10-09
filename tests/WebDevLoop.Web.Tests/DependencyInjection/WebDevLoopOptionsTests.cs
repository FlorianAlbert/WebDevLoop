using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.Tests.DependencyInjection;

public sealed class WebDevLoopOptionsTests
{
    [Fact]
    public void defaults_keep_every_file_under_the_users_local_app_data_directory()
    {
        WebDevLoopOptions options = WebDevLoopOptions.Load(Configuration());

        string expectedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebDevLoop");
        Assert.Equal(expectedRoot, options.ResolvedDataDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "webdevloop.db"), options.ResolvedDatabasePath);
        Assert.Contains(Path.Combine(expectedRoot, "webdevloop.db"), options.ConnectionString);
        Assert.True(options.Workflow.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(2), options.Workflow.RecoveryInterval);
    }

    [Fact]
    public void data_directory_database_path_and_timers_are_configurable()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "options-data");

        WebDevLoopOptions options = WebDevLoopOptions.Load(Configuration(
            ("WebDevLoop:DataDirectory", root),
            ("WebDevLoop:DatabasePath", Path.Combine(root, "db", "custom.db")),
            ("WebDevLoop:Workflow:RecoveryInterval", "00:00:30"),
            ("WebDevLoop:GitHub:GhStackMode", "FallbackRequired"),
            ("WebDevLoop:AgentLogs:MaxEntriesPerStep", "250")));

        Assert.Equal(root, options.ResolvedDataDirectory);
        Assert.Equal(Path.Combine(root, "db", "custom.db"), options.ResolvedDatabasePath);
        Assert.Equal(Path.Combine(root, "ui-state.json"), options.UiStatePath);
        Assert.Equal(Path.Combine(root, "github-credentials.dat"), options.GitHubCredentialsPath);
        Assert.Equal(Path.Combine(root, "keys"), options.DataProtectionKeysDirectory);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Workflow.RecoveryInterval);
        Assert.Equal(Infrastructure.Prerequisites.GhStackMode.FallbackRequired, options.GitHub.GhStackMode);
        Assert.Equal(250, options.AgentLogs.MaxEntriesPerStep);
    }

    [Fact]
    public void the_github_app_sign_in_is_configurable()
    {
        WebDevLoopOptions options = WebDevLoopOptions.Load(Configuration(
            ("WebDevLoop:GitHub:AppClientId", "Iv23.client"),
            ("WebDevLoop:GitHub:AppClientSecret", "client-secret"),
            ("WebDevLoop:GitHub:AppSlug", "webdevloop"),
            ("WebDevLoop:GitHub:WebBaseUrl", "https://github.example/")));

        Assert.Equal(
            ("Iv23.client", "client-secret", "webdevloop", "https://github.example/"),
            (options.GitHub.AppClientId, options.GitHub.AppClientSecret, options.GitHub.AppSlug, options.GitHub.WebBaseUrl));
    }

    [Fact]
    public void invalid_configuration_fails_fast_listing_every_problem()
    {
        var exception = Assert.Throws<WebDevLoopConfigurationException>(() => WebDevLoopOptions.Load(Configuration(
            ("WebDevLoop:Workflow:RecoveryInterval", "00:00:00"),
            ("WebDevLoop:GitHub:WebBaseUrl", "github.com"),
            ("WebDevLoop:GitHub:ApiBaseUrl", "not a url"))));

        Assert.Equal(3, exception.Errors.Count);
        Assert.Contains("WebDevLoop:Workflow:RecoveryInterval", exception.Message);
        Assert.Contains("WebDevLoop:GitHub:WebBaseUrl", exception.Message);
        Assert.Contains("WebDevLoop:GitHub:ApiBaseUrl", exception.Message);
    }

    [Fact]
    public void the_app_refuses_to_start_with_an_invalid_configuration()
    {
        using var factory = new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder =>
            builder.UseSetting("WebDevLoop:DataDirectory", Path.Combine(AppContext.BaseDirectory, "never-created"))
                .UseSetting("WebDevLoop:Workflow:OutboxPollInterval", "-00:00:01"));

        var exception = Assert.Throws<WebDevLoopConfigurationException>(() => factory.CreateClient());

        Assert.Contains("WebDevLoop:Workflow:OutboxPollInterval", exception.Message);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();
}
