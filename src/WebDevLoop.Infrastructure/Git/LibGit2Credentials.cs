using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Git;

public static class LibGit2Credentials
{
    /// <summary>Adapts a credential callback to LibGit2Sharp; each libgit2 authentication challenge resolves the current token.</summary>
    public static CredentialsHandler CreateHandler(Func<GitHttpsCredential> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return (_, _, _) =>
        {
            GitHttpsCredential credential = resolve();
            return new UsernamePasswordCredentials { Username = credential.Username, Password = credential.Password };
        };
    }
}
