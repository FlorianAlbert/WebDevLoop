using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Components.Layout;

namespace WebDevLoop.Web.GitHubAuth;

/// <summary>
/// The browser side of the GitHub App's web application flow. <c>login</c> remembers a random state and a PKCE code
/// verifier in a short-lived, encrypted cookie and redirects to GitHub; GitHub redirects back to <c>callback</c>, which
/// checks the state, exchanges the code for the user's tokens and returns to the page the sign-in started from. Failures
/// land on the GitHub page with a message.
/// </summary>
public static class GitHubSignInEndpoints
{
    public const string LoginPath = "/auth/github/login";
    public const string CallbackPath = "/auth/github/callback";

    private const string CookieName = "WebDevLoop.GitHubSignIn";
    private const string CookiePath = "/auth/github";
    private const string Purpose = "WebDevLoop.GitHubSignIn.v1";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Starts a sign-in that returns to <paramref name="returnUrl"/> (a local path) when it completes.</summary>
    public static string LoginUrl(string? returnUrl = null) =>
        string.IsNullOrEmpty(returnUrl) ? LoginPath : $"{LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";

    public static IEndpointRouteBuilder MapGitHubSignIn(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(LoginPath, Login).ExcludeFromDescription();
        endpoints.MapGet(CallbackPath, CallbackAsync).ExcludeFromDescription();
        return endpoints;
    }

    private static IResult Login(HttpContext http, [FromServices] GitHubUserSession session, [FromServices] IDataProtectionProvider protection, string? returnUrl)
    {
        if (!session.Status.IsConfigured)
        {
            return FailureRedirect("GitHub sign-in is not configured: set the GitHub App's client id and client secret (see the README).");
        }

        var pending = new PendingSignIn(RandomToken(), RandomToken(), IsLocalUrl(returnUrl) ? returnUrl! : UiRoutes.GitHub);
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(pending.CodeVerifier)));
        string cookie = Protector(protection).Protect(JsonSerializer.Serialize(pending), PendingLifetime);

        // Lax: the cookie must come along on GitHub's top-level redirect back to the callback.
        http.Response.Cookies.Append(CookieName, cookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = CookiePath,
            MaxAge = PendingLifetime,
            IsEssential = true,
        });
        return Results.Redirect(session.CreateAuthorizationUri(RedirectUri(http), pending.State, challenge).ToString());
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext http,
        [FromServices] GitHubUserSession session,
        [FromServices] DiagnosticReadiness readiness,
        [FromServices] IDataProtectionProvider protection,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        [FromQuery(Name = "setup_action")] string? setupAction,
        CancellationToken cancellationToken)
    {
        PendingSignIn? pending = ReadPending(http, protection);
        http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath });

        if (pending is null && setupAction is not null)
        {
            // GitHub returns here after the App was installed or its repositories changed (not a sign-in this app started).
            return Results.LocalRedirect(UiRoutes.GitHub);
        }

        if (pending is null || state is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(pending.State)))
        {
            return FailureRedirect("The sign-in expired or was not started by this app. Sign in with GitHub again.");
        }

        if (error is not null || string.IsNullOrEmpty(code))
        {
            return FailureRedirect($"GitHub did not complete the sign-in: {errorDescription ?? error ?? "no authorization code was returned"}.");
        }

        try
        {
            await session.CompleteSignInAsync(code, RedirectUri(http), pending.CodeVerifier, cancellationToken);
        }
        catch (GitHubSignInException exception)
        {
            return FailureRedirect(exception.Message);
        }

        await readiness.RefreshAsync(cancellationToken);
        return Results.LocalRedirect(pending.ReturnUrl);
    }

    private static PendingSignIn? ReadPending(HttpContext http, IDataProtectionProvider protection)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out string? cookie) || string.IsNullOrEmpty(cookie))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PendingSignIn>(Protector(protection).Unprotect(cookie));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Must match a callback URL of the GitHub App (for example <c>https://localhost:7233/auth/github/callback</c>).</summary>
    private static Uri RedirectUri(HttpContext http) =>
        new($"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}{CallbackPath}");

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider protection) =>
        protection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    private static IResult FailureRedirect(string message) =>
        Results.LocalRedirect($"{UiRoutes.GitHub}?error={Uri.EscapeDataString(message)}");

    private static string RandomToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    private sealed record PendingSignIn(string State, string CodeVerifier, string ReturnUrl);
}
