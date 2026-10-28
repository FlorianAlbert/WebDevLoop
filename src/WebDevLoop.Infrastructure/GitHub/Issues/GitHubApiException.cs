using System.Net;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

public sealed class GitHubApiException(GitHubApiErrorKind kind, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : GitHubIssuesException(message, innerException), ITransientFault
{
    public GitHubApiErrorKind Kind { get; } = kind;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    public bool IsRetryable => Kind == GitHubApiErrorKind.Transient;

    bool ITransientFault.IsTransient => IsRetryable;
}
