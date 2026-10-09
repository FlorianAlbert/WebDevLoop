namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>A rotating user Copilot token and its remaining lifetime.</summary>
internal sealed record CopilotUserToken(string Value, TimeSpan ExpiresIn);

/// <summary>
/// Session-level Copilot credentials. App installation tokens live in the runtime environment instead
/// (<see cref="None"/>); user/PAT tokens are passed per session, through a callback when they rotate.
/// </summary>
internal sealed record CopilotSessionAuth
{
    private CopilotSessionAuth(string? gitHubToken, Func<CancellationToken, Task<CopilotUserToken>>? tokenProvider)
    {
        GitHubToken = gitHubToken;
        TokenProvider = tokenProvider;
    }

    public static CopilotSessionAuth None { get; } = new(null, null);

    public string? GitHubToken { get; }

    public Func<CancellationToken, Task<CopilotUserToken>>? TokenProvider { get; }

    public static CopilotSessionAuth StaticToken(string token) => new(token, null);

    public static CopilotSessionAuth RotatingToken(Func<CancellationToken, Task<CopilotUserToken>> provider) => new(null, provider);
}
