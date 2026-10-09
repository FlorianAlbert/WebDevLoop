namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>A rotating user Copilot token and its remaining lifetime.</summary>
internal sealed record CopilotUserToken(string Value, TimeSpan ExpiresIn);

/// <summary>Session-level Copilot credentials: the signed-in user's token, through a callback when it expires and is refreshed.</summary>
internal sealed record CopilotSessionAuth
{
    private CopilotSessionAuth(string? gitHubToken, Func<CancellationToken, Task<CopilotUserToken>>? tokenProvider)
    {
        GitHubToken = gitHubToken;
        TokenProvider = tokenProvider;
    }

    public string? GitHubToken { get; }

    public Func<CancellationToken, Task<CopilotUserToken>>? TokenProvider { get; }

    public static CopilotSessionAuth StaticToken(string token) => new(token, null);

    public static CopilotSessionAuth RotatingToken(Func<CancellationToken, Task<CopilotUserToken>> provider) => new(null, provider);
}
