using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class GitWorkspaceRemoteSyncTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();
    private readonly GitWorkspace _workspace;

    public GitWorkspaceRemoteSyncTests() => _workspace = _sandbox.CreateWorkspace();

    [Fact]
    public async Task ensure_cloned_clones_a_missing_repository_with_remote_tracking_refs()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(_sandbox.Location.LocalPath, ".git")));
        Assert.Equal(_sandbox.InitialCommit, await _workspace.GetBranchTipAsync(_sandbox.Location, GitSandbox.Main, GitRefScope.Remote, CancellationToken.None));
    }

    [Fact]
    public async Task ensure_cloned_fetches_when_the_clone_already_exists()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        CommitSha newer = _sandbox.CommitToRemote(GitSandbox.Main, new Dictionary<string, string> { ["a.txt"] = "a" }, "newer");

        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);

        Assert.Equal(newer, await _workspace.GetBranchTipAsync(_sandbox.Location, GitSandbox.Main, GitRefScope.Remote, CancellationToken.None));
    }

    [Fact]
    public async Task fetch_updates_remote_tracking_refs_without_moving_local_branches()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        var feature = new BranchName("feature/x");
        CommitSha featureTip = _sandbox.CommitToRemote(feature, new Dictionary<string, string> { ["b.txt"] = "b" }, "feature");

        await _workspace.FetchAsync(_sandbox.Location, CancellationToken.None);

        Assert.Equal(featureTip, await _workspace.GetBranchTipAsync(_sandbox.Location, feature, GitRefScope.Remote, CancellationToken.None));
        Assert.Null(await _workspace.GetBranchTipAsync(_sandbox.Location, feature, GitRefScope.Local, CancellationToken.None));
    }

    [Fact]
    public async Task each_remote_operation_requests_its_own_credential_callback()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        await _workspace.FetchAsync(_sandbox.Location, CancellationToken.None);
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);

        Assert.Equal(
            [(GitSandbox.Repo, GitRemoteOperation.Clone), (GitSandbox.Repo, GitRemoteOperation.Fetch), (GitSandbox.Repo, GitRemoteOperation.Fetch)],
            _sandbox.Credentials.Requests);
    }

    [Fact]
    public void credentials_handler_resolves_a_fresh_credential_on_every_libgit2_callback()
    {
        var issued = 0;
        var handler = LibGit2Credentials.CreateHandler(() => new GitHttpsCredential("x-access-token", $"token-{++issued}"));

        var first = Assert.IsType<LibGit2Sharp.UsernamePasswordCredentials>(handler("https://github.com/acme/widgets.git", null, LibGit2Sharp.SupportedCredentialTypes.UsernamePassword));
        var second = Assert.IsType<LibGit2Sharp.UsernamePasswordCredentials>(handler("https://github.com/acme/widgets.git", null, LibGit2Sharp.SupportedCredentialTypes.UsernamePassword));

        Assert.Equal(("x-access-token", "token-1"), (first.Username, first.Password));
        Assert.Equal("token-2", second.Password);
    }

    public void Dispose() => _sandbox.Dispose();
}
