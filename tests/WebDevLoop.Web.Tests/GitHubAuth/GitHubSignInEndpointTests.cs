using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Tests.Api;

namespace WebDevLoop.Web.Tests.GitHubAuth;

public sealed class GitHubSignInEndpointTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();
    private HttpClient _client = default!;

    public async ValueTask InitializeAsync()
    {
        await _factory.EvaluateAsync();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task login_redirects_to_github_with_state_and_a_pkce_challenge()
    {
        HttpResponseMessage response = await _client.GetAsync("/auth/github/login?returnUrl=/queue", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri location = response.Headers.Location!;
        Assert.Equal("https://github.com/login/oauth/authorize", location.GetLeftPart(UriPartial.Path));
        Dictionary<string, string> query = Query(location);
        Assert.Equal(StubGitHub.ClientId, query["client_id"]);
        Assert.Equal("http://localhost/auth/github/callback", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(query["state"], cookie);
    }

    [Fact]
    public async Task callback_completes_the_sign_in_with_the_matching_verifier_and_returns_to_the_start_page()
    {
        Dictionary<string, string> authorize = await StartAsync("/queue");

        HttpResponseMessage response = await _client.GetAsync($"/auth/github/callback?code=code-1&state={Uri.EscapeDataString(authorize["state"])}", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/queue", response.Headers.Location!.OriginalString);
        string form = Assert.Single(_factory.GitHub.Requests, request => request.Uri.AbsolutePath == "/login/oauth/access_token").Body;
        Dictionary<string, string> exchange = QueryHelpers.ParseQuery(form).ToDictionary(entry => entry.Key, entry => entry.Value.ToString());
        Assert.Equal("code-1", exchange["code"]);
        Assert.Equal(authorize["code_challenge"], Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(exchange["code_verifier"]))));
        Assert.Equal("ghu_signed_in", _factory.GitHubCredentials.Stored!.AccessToken);
        Assert.Equal("octocat", _factory.GitHubCredentials.Stored.Login);
    }

    [Fact]
    public async Task callback_refreshes_the_readiness_so_the_workflow_can_start()
    {
        await _factory.EvaluateAsync(new PrerequisiteCheck("GitHub authentication", PrerequisiteStatus.Failed, "Nobody is signed in."));
        Dictionary<string, string> authorize = await StartAsync("/");
        _factory.Validator.Checks = [new PrerequisiteCheck("GitHub authentication", PrerequisiteStatus.Passed, "Signed in.")];

        await _client.GetAsync($"/auth/github/callback?code=code-1&state={Uri.EscapeDataString(authorize["state"])}", Ct);

        Assert.Equal(ReadinessMode.Operational, _factory.Readiness.Current.Mode);
    }

    [Fact]
    public async Task callback_with_a_foreign_state_is_rejected_without_exchanging_the_code()
    {
        await StartAsync("/queue");

        HttpResponseMessage response = await _client.GetAsync("/auth/github/callback?code=code-1&state=forged", Ct);

        AssertFailure(response, "not started by this app");
        Assert.DoesNotContain(_factory.GitHub.Requests, request => request.Uri.AbsolutePath == "/login/oauth/access_token");
        Assert.Null(_factory.GitHubCredentials.Stored);
    }

    [Fact]
    public async Task callback_without_a_pending_sign_in_is_rejected()
    {
        HttpResponseMessage response = await _client.GetAsync("/auth/github/callback?code=code-1&state=anything", Ct);

        AssertFailure(response, "not started by this app");
    }

    [Fact]
    public async Task denied_authorization_shows_githubs_reason()
    {
        Dictionary<string, string> authorize = await StartAsync("/");

        HttpResponseMessage response = await _client.GetAsync(
            $"/auth/github/callback?error=access_denied&error_description=The+user+has+denied+your+application+access.&state={Uri.EscapeDataString(authorize["state"])}", Ct);

        AssertFailure(response, "The user has denied your application access.");
    }

    [Fact]
    public async Task a_rejected_code_shows_the_reason_and_stays_signed_out()
    {
        _factory.GitHub.IssuedAccessToken = null;
        Dictionary<string, string> authorize = await StartAsync("/");

        HttpResponseMessage response = await _client.GetAsync($"/auth/github/callback?code=stale&state={Uri.EscapeDataString(authorize["state"])}", Ct);

        AssertFailure(response, "The code passed is incorrect or expired.");
        Assert.Null(_factory.GitHubCredentials.Stored);
    }

    [Fact]
    public async Task returning_from_an_app_installation_goes_to_the_github_page()
    {
        HttpResponseMessage response = await _client.GetAsync("/auth/github/callback?installation_id=7&setup_action=update", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/github", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task only_local_return_urls_are_honoured(string returnUrl)
    {
        Dictionary<string, string> authorize = await StartAsync(returnUrl);

        HttpResponseMessage response = await _client.GetAsync($"/auth/github/callback?code=code-1&state={Uri.EscapeDataString(authorize["state"])}", Ct);

        Assert.Equal("/github", response.Headers.Location!.OriginalString);
    }

    private async Task<Dictionary<string, string>> StartAsync(string returnUrl)
    {
        HttpResponseMessage response = await _client.GetAsync($"/auth/github/login?returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Query(response.Headers.Location!);
    }

    private static void AssertFailure(HttpResponseMessage response, string expectedMessage)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/github?error=", location);
        Assert.Contains(expectedMessage, Uri.UnescapeDataString(location));
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        QueryHelpers.ParseQuery(uri.Query).ToDictionary(entry => entry.Key, entry => entry.Value.ToString());
}
