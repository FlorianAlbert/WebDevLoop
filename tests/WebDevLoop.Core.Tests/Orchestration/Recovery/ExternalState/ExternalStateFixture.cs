using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

/// <summary>
/// Wires the external-state reconciler to the integration fixture's in-memory store, Git, and GitHub. Git/GitHub side
/// effects go through the fixture's journaling decorators, so tests can crash a saga at any point and count the calls
/// the reconciliation pass makes afterwards.
/// </summary>
internal sealed class ExternalStateFixture
{
    public IntegrationFixture Integration { get; } = new();

    public RecordingIntegrationLauncher Launcher { get; } = new();

    public ExternalReconciliationOptions Options { get; set; } = new();

    /// <summary>Wraps the Git port the reconciler sees (e.g. to fail fetches).</summary>
    public Func<IGitWorkspace, IGitWorkspace> DecorateGit { get; set; } = git => git;

    public static CancellationToken Token => IntegrationFixture.Token;

    public ExternalStateReconciler Reconciler()
    {
        IntegrationFixture f = Integration;
        var unitOfWork = new CrashingUnitOfWork(f.Store, f.Journal);
        IGitWorkspace git = DecorateGit(new JournalingGitWorkspace(f.Git, f.Journal));
        var issues = new JournalingGitHubIssues(f.Issues, f.Journal);
        IGitHubPullsAndStacks pulls = f.JournaledPulls;
        var completion = new SpecCompletionService(f.Store, f.Store, f.Store, f.Store, f.Store, f.Store, git, pulls, issues, f.Gate, f.Store, unitOfWork, f.Clock);
        var tracking = new MergeTrackingService(f.Store, f.Store, f.Store, f.Store, pulls, completion, f.Store, unitOfWork, f.Clock, new ReadyAndMergeOptions());
        return new ExternalStateReconciler(
            f.Store,
            f.Store,
            git,
            new IntegrationBranchReconciler(git),
            new TicketGraphReconciler(issues, f.Store, f.Store, f.Store, f.Ids, f.Store, unitOfWork, f.Clock),
            new FindingIssuanceReconciler(f.Store, f.Store, issues, f.Ids, unitOfWork, f.Clock),
            new WorktreeReconciler(f.Store, f.Store, git),
            new IntegrationSagaReconciler(f.Store, f.Store, pulls, f.Runner(unitOfWork), Launcher, f.Store, unitOfWork, f.Clock, Options),
            new StackBaseReconciler(f.Store, pulls, f.Store, unitOfWork, f.Clock),
            tracking);
    }

    public Task<ExternalReconciliationReport> ReconcileAsync() => Reconciler().ReconcileAsync(Token);

    /// <summary>A running spec on the trunk tip whose parent issue exists on GitHub, so its ticket graph can be read.</summary>
    public SpecRun RunningSpec(SpecDependencyMode? mode = null)
    {
        SpecRun spec = Integration.SeedRunningSpec(mode: mode);
        Integration.Issues.Seed(spec.ParentIssue, spec.Title);
        return spec;
    }

    /// <summary>A running spec for <paramref name="parentIssue"/> (e.g. a later run of an earlier spec issue), starting at the trunk tip.</summary>
    public SpecRun SeedRunningSpec(IssueRef parentIssue)
    {
        IntegrationFixture f = Integration;
        SpecRun spec = SpecRun.Queue(f.Ids.NewRunId(), f.Repository.Id, parentIssue, $"Spec {parentIssue.Number}", "spec body", parentIssue.Number, IntegrationFixture.T0);
        spec.TransitionTo(SpecRunStatus.Preparing, IntegrationFixture.T0);
        spec.BaseBranch = IntegrationFixture.Trunk;
        spec.IntegrationBaseSha = f.TrunkTip;
        spec.IntegrationTipSha = f.TrunkTip;
        spec.TransitionTo(SpecRunStatus.Running, IntegrationFixture.T0);
        f.Git.UpdateBranchAsync(f.Location, spec.IntegrationBranch, f.TrunkTip, null, Token).GetAwaiter().GetResult();
        f.Store.Add(spec);
        return spec;
    }

    /// <summary>A reviewed ticket of <paramref name="spec"/> for an issue that already exists on GitHub (no new issue is seeded).</summary>
    public TicketRun SeedReviewedTicketForExistingIssue(SpecRun spec, IssueRef issue, params string[] files)
    {
        IntegrationFixture f = Integration;
        TicketRun ticket = TicketRun.Create(f.Ids.NewTicketRunId(), spec.Id, issue, $"Ticket {issue.Number}", $"Implement {issue.Number}.", IntegrationFixture.T0);
        string worktreePath = $"{IntegrationFixture.WorkspaceRoot}/runs/{spec.Id}/{ticket.Id}";
        f.Git.PrepareWorktreeAsync(f.Location, new WorktreeSpec(ticket.BranchName, spec.IntegrationTipSha!.Value, worktreePath), Token).GetAwaiter().GetResult();
        ticket.WorktreePath = worktreePath;
        ticket.LastImplementedSha = f.Git.CommitInWorktree(worktreePath, files);
        ticket.TransitionTo(TicketRunStatus.Ready, IntegrationFixture.T0);
        ticket.TransitionTo(TicketRunStatus.Implementing, IntegrationFixture.T0);
        ticket.TransitionTo(TicketRunStatus.Reviewing, IntegrationFixture.T0);
        f.Store.Add(ticket);
        return ticket;
    }

    /// <summary>Integrates <paramref name="ticket"/> until the process crashes right after save number <paramref name="crashAfterSave"/>, then restarts.</summary>
    public async Task CrashIntegrationAfterSaveAsync(TicketRun ticket, int crashAfterSave)
    {
        IntegrationFixture f = Integration;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.IntegrateAsync(ticket, new CrashingUnitOfWork(f.Store, f.Journal, crashAfterSave)));
        f.Journal.Restart();
    }

    /// <summary>Integrates <paramref name="ticket"/> until the process crashes right after the external call starting with <paramref name="callPrefix"/>.</summary>
    public async Task CrashIntegrationAfterCallAsync(TicketRun ticket, string callPrefix)
    {
        IntegrationFixture f = Integration;
        f.Journal.AfterCall = call =>
        {
            if (call.StartsWith(callPrefix, StringComparison.Ordinal))
            {
                f.Journal.AfterCall = null;
                f.Journal.Crash($"after {call}");
            }
        };
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.IntegrateAsync(ticket));
        f.Journal.Restart();
    }

    public IReadOnlyList<string> CallsSince(int count) => Integration.Journal.Calls.Skip(count).ToArray();

    public IEnumerable<T> Pending<T>() where T : WorkflowEvent => Integration.Store.PendingEvents.OfType<T>();

    public IReadOnlyList<TicketRun> Tickets(SpecRun spec) =>
        ((ITicketRunRepository)Integration.Store).ListBySpecRunAsync(spec.Id, Token).GetAwaiter().GetResult();

    public IReadOnlyList<TicketDependency> Dependencies(SpecRun spec) =>
        ((ITicketRunRepository)Integration.Store).ListDependenciesAsync(spec.Id, Token).GetAwaiter().GetResult();

    public IReadOnlyList<RunEvent> RunEvents(SpecRun spec) =>
        ((IRunEventRepository)Integration.Store).ListBySpecRunAsync(spec.Id, Token).GetAwaiter().GetResult();
}

internal sealed class RecordingIntegrationLauncher : IIntegrationLauncher
{
    public List<IntegrationAssignment> Launched { get; } = [];

    public void Launch(IntegrationAssignment assignment) => Launched.Add(assignment);
}
