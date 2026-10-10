using System.Net;
using WebDevLoop.Core.Orchestration.Integration;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

public sealed class GitHubApiException(HttpStatusCode statusCode, string message) : Exception(message), ITransientFault
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Server errors, rate limiting and request timeouts may succeed on a later attempt.</summary>
    public bool IsTransient => (int)StatusCode >= 500 || StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout;
}
