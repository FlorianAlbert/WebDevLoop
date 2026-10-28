using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Management;

/// <summary>A read-only label check: confirmed presence, confirmed absence, or a failure to report and retry.</summary>
public abstract record RepositoryLabelCheck
{
    private RepositoryLabelCheck()
    {
    }

    /// <summary>Returns null when work may proceed, otherwise the caller's missing-label or lookup-failure result.</summary>
    public TResult? ToDeferredResult<TResult>(TResult missing, Func<GitHubIssuesException, TResult> lookupFailed)
        where TResult : class =>
        this switch
        {
            Available => null,
            Missing => missing,
            LookupFailed(var error) => lookupFailed(error),
            _ => throw new InvalidOperationException("Unknown repository label check result."),
        };

    public sealed record Available : RepositoryLabelCheck;

    public sealed record Missing : RepositoryLabelCheck;

    public sealed record LookupFailed(GitHubIssuesException Error) : RepositoryLabelCheck;
}
