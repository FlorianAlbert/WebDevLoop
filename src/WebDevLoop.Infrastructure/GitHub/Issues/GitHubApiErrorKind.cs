namespace WebDevLoop.Infrastructure.GitHub.Issues;

public enum GitHubApiErrorKind
{
    /// <summary>Network failure, timeout, 5xx, or rate limiting: the same call may succeed later.</summary>
    Transient,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Invalid,
}
