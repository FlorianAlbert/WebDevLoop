using System.Net;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

public sealed class GitHubApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
