using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class GitWorkspacePushTests : IAsyncLifetime
{
    private static readonly RunId Run1 = new("run-1");
    private static readonly RunId Run2 = new("run-2");
    private static readonly TicketRunId Ticket = new("t-1");
    private static readonly BranchName Integration = RunScopedNaming.IntegrationBranch(Run1);
    private readonly GitSandbox _sandbox = new();
    private readonly GitWorkspace _workspace;
    private CommitSha _squashA;
    private CommitSha _squashB;

    public GitWorkspacePushTests() => _workspace = _sandbox.CreateWorkspace();

    public async ValueTask InitializeAsync()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        TicketWorktree worktree = await _workspace.PrepareWorktreeAsync(
            _sandbox.Location, new WorktreeSpec(RunScopedNaming.TicketBranch(Run1, Ticket), _sandbox.InitialCommit, _sandbox.WorktreePath("t-1")), CancellationToken.None);
        CommitSha first = GitSandbox.CommitInWorktree(worktree.Path, new Dictionary<string, string> { ["a.txt"] = "a" }, "a");
        _squashA = (await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(first, _sandbox.InitialCommit, "A"), CancellationToken.None)).Commit!.Value;
        CommitSha second = GitSandbox.CommitInWorktree(worktree.Path, new Dictionary<string, string> { ["b.txt"] = "b" }, "b");
        _squashB = (await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(second, _squashA, "B"), CancellationToken.None)).Commit!.Value;
    }

    [Fact]
    public async Task new_immutable_stack_ref_is_pushed_and_tracked()
    {
        BranchName stack = RunScopedNaming.StackBranch(Run1, Ticket);

        PushOutcome outcome = await _workspace.PushAsync(_sandbox.Location, new RefPush(stack, _squashA, ExpectedRemoteTip: null), CancellationToken.None);

        Assert.Equal(PushOutcome.Pushed, outcome);
        Assert.Equal(_squashA, _sandbox.RemoteTip(stack));
        Assert.Equal(_squashA, await _workspace.GetBranchTipAsync(_sandbox.Location, stack, GitRefScope.Remote, CancellationToken.None));
    }

    [Fact]
    public async Task repeated_spec_run_does_not_reuse_or_move_an_older_stack_branch()
    {
        BranchName olderStack = RunScopedNaming.StackBranch(Run1, Ticket);
        BranchName newerStack = RunScopedNaming.StackBranch(Run2, Ticket);
        await _workspace.PushAsync(_sandbox.Location, new RefPush(olderStack, _squashA, null), CancellationToken.None);

        PushOutcome newer = await _workspace.PushAsync(_sandbox.Location, new RefPush(newerStack, _squashB, null), CancellationToken.None);
        PushOutcome overwrite = await _workspace.PushAsync(_sandbox.Location, new RefPush(olderStack, _squashB, null), CancellationToken.None);

        Assert.NotEqual(olderStack, newerStack);
        Assert.Equal(PushOutcome.Pushed, newer);
        Assert.Equal(PushOutcome.Rejected, overwrite);
        Assert.Equal(_squashA, _sandbox.RemoteTip(olderStack));
        Assert.Equal(_squashB, _sandbox.RemoteTip(newerStack));
    }

    [Fact]
    public async Task replaying_a_completed_push_is_already_up_to_date()
    {
        BranchName stack = RunScopedNaming.StackBranch(Run1, Ticket);
        await _workspace.PushAsync(_sandbox.Location, new RefPush(stack, _squashA, null), CancellationToken.None);

        PushOutcome replay = await _workspace.PushAsync(_sandbox.Location, new RefPush(stack, _squashA, null), CancellationToken.None);

        Assert.Equal(PushOutcome.AlreadyUpToDate, replay);
    }

    [Fact]
    public async Task integration_ref_advances_when_the_remote_is_at_the_expected_tip()
    {
        await _workspace.PushAsync(_sandbox.Location, new RefPush(Integration, _squashA, null), CancellationToken.None);

        PushOutcome outcome = await _workspace.PushAsync(_sandbox.Location, new RefPush(Integration, _squashB, _squashA), CancellationToken.None);

        Assert.Equal(PushOutcome.Pushed, outcome);
        Assert.Equal(_squashB, _sandbox.RemoteTip(Integration));
    }

    [Fact]
    public async Task push_is_rejected_when_the_remote_moved_away_from_the_expected_tip()
    {
        await _workspace.PushAsync(_sandbox.Location, new RefPush(Integration, _squashA, null), CancellationToken.None);
        CommitSha foreign = _sandbox.CommitToRemote(Integration, new Dictionary<string, string> { ["foreign.txt"] = "f" }, "foreign");

        PushOutcome outcome = await _workspace.PushAsync(_sandbox.Location, new RefPush(Integration, _squashB, _squashA), CancellationToken.None);

        Assert.Equal(PushOutcome.Rejected, outcome);
        Assert.Equal(foreign, _sandbox.RemoteTip(Integration));
    }

    [Fact]
    public async Task non_fast_forward_push_is_rejected_even_when_the_lease_matches()
    {
        await _workspace.PushAsync(_sandbox.Location, new RefPush(Integration, _squashB, null), CancellationToken.None);

        PushOutcome outcome = await _workspace.PushAsync(_sandbox.Location, new RefPush(Integration, _squashA, _squashB), CancellationToken.None);

        Assert.Equal(PushOutcome.Rejected, outcome);
        Assert.Equal(_squashB, _sandbox.RemoteTip(Integration));
    }

    [Fact]
    public async Task every_push_resolves_a_fresh_push_credential()
    {
        BranchName stack = RunScopedNaming.StackBranch(Run1, Ticket);

        await _workspace.PushAsync(_sandbox.Location, new RefPush(stack, _squashA, null), CancellationToken.None);
        await _workspace.PushAsync(_sandbox.Location, new RefPush(Integration, _squashA, null), CancellationToken.None);

        Assert.Equal(2, _sandbox.Credentials.Requests.Count(r => r == (GitSandbox.Repo, GitRemoteOperation.Push)));
    }

    public ValueTask DisposeAsync()
    {
        _sandbox.Dispose();
        return ValueTask.CompletedTask;
    }
}
