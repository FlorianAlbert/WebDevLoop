using System.Net;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

public sealed class GitHubApiException(GitHubApiErrorKind kind, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public GitHubApiErrorKind Kind { get; } = kind;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    public bool IsRetryable => Kind == GitHubApiErrorKind.Transient;
}
