using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Git;

/// <summary>Clone, fetch and push through LibGit2Sharp, resolving credentials per remote operation.</summary>
internal sealed class GitRemoteSync(IGitCredentialSource credentials, bool allowUserTokenFallback)
{
    public const string RemoteName = "origin";

    public void EnsureCloned(GitRepositoryLocation location, string localPath, CancellationToken cancellationToken)
    {
        if (Repository.IsValid(localPath))
        {
            using var existing = new Repository(localPath);
            EnsureRemoteUrl(existing, location.CloneUrl);
            Fetch(existing, location, cancellationToken);
            return;
        }

        if (Directory.Exists(localPath) && Directory.EnumerateFileSystemEntries(localPath).Any())
        {
            throw new InvalidOperationException($"'{localPath}' exists but is not a Git repository.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        var clone = new CloneOptions(FetchOptionsFor(location, GitRemoteOperation.Clone, cancellationToken));
        Repository.Clone(location.CloneUrl, localPath, clone);
    }

    public void Fetch(Repository repo, GitRepositoryLocation location, CancellationToken cancellationToken)
    {
        Remote remote = repo.Network.Remotes[RemoteName];
        Commands.Fetch(
            repo,
            RemoteName,
            remote.FetchRefSpecs.Select(spec => spec.Specification),
            FetchOptionsFor(location, GitRemoteOperation.Fetch, cancellationToken),
            logMessage: null);
    }

    /// <summary>
    /// Pushes with lease semantics: the remote ref is read first and must equal <see cref="RefPush.ExpectedRemoteTip"/>.
    /// Pushes are never forced, so a non-fast-forward update or a remote that moves after the check is rejected by Git.
    /// </summary>
    public PushOutcome Push(Repository repo, GitRepositoryLocation location, RefPush push, CancellationToken cancellationToken)
    {
        Commit commit = GitRefs.RequireCommit(repo, push.Commit);
        string target = GitRefs.Canonical(push.Branch);
        CredentialsHandler credentialsHandler =
            LibGit2Credentials.CreateHandler(credentials.CreateCallback(location.Repo, GitRemoteOperation.Push, allowUserTokenFallback));
        Remote remote = repo.Network.Remotes[RemoteName];

        CommitSha? remoteTip = RemoteTip(repo, remote, target, credentialsHandler);
        if (remoteTip == push.Commit)
        {
            Track(repo, push.Branch, commit);
            return PushOutcome.AlreadyUpToDate;
        }

        if (remoteTip != push.ExpectedRemoteTip || (remoteTip is { } current && !GitRefs.IsAncestor(repo, current, push.Commit)))
        {
            return PushOutcome.Rejected;
        }

        string? rejection = null;
        var options = new PushOptions
        {
            CredentialsProvider = credentialsHandler,
            OnPushStatusError = error => rejection = error.Message,
            OnPushTransferProgress = (_, _, _) => !cancellationToken.IsCancellationRequested,
        };
        try
        {
            repo.Network.Push(remote, $"{commit.Sha}:{target}", options);
        }
        catch (NonFastForwardException)
        {
            return PushOutcome.Rejected;
        }

        if (rejection is not null)
        {
            return PushOutcome.Rejected;
        }

        Track(repo, push.Branch, commit);
        return PushOutcome.Pushed;
    }

    private static CommitSha? RemoteTip(Repository repo, Remote remote, string canonicalName, CredentialsHandler credentialsHandler) =>
        repo.Network.ListReferences(remote, credentialsHandler)
            .OfType<DirectReference>()
            .FirstOrDefault(reference => reference.CanonicalName == canonicalName)?.TargetIdentifier is { } sha
            ? new CommitSha(sha)
            : null;

    private static void Track(Repository repo, BranchName branch, Commit commit) =>
        repo.Refs.Add(GitRefs.Canonical(branch, GitRefScope.Remote), commit.Id, "webdevloop: push", allowOverwrite: true);

    private FetchOptions FetchOptionsFor(GitRepositoryLocation location, GitRemoteOperation operation, CancellationToken cancellationToken) => new()
    {
        CredentialsProvider = LibGit2Credentials.CreateHandler(credentials.CreateCallback(location.Repo, operation, allowUserTokenFallback)),
        OnTransferProgress = _ => !cancellationToken.IsCancellationRequested,
        Prune = true,
    };

    private static void EnsureRemoteUrl(Repository repo, string cloneUrl)
    {
        if (repo.Network.Remotes[RemoteName] is null)
        {
            repo.Network.Remotes.Add(RemoteName, cloneUrl);
        }
        else if (repo.Network.Remotes[RemoteName].Url != cloneUrl)
        {
            repo.Network.Remotes.Update(RemoteName, remote => remote.Url = cloneUrl);
        }
    }
}
