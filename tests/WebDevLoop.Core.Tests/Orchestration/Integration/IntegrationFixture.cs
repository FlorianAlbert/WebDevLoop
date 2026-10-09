using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

/// <summary>
/// Wires the integration saga to the in-memory store, Git, GitHub, and scripted agents. Git/GitHub side effects go through
/// journaling decorators so tests can count them, hold them, or crash right after them.
/// </summary>
internal sealed class IntegrationFixture
{
    public const string WorkspaceRoot = "/work";
    public const string SkillsRoot = "/app/skills";

    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    public static readonly BranchName Trunk = new("main");
    public static readonly GitHubRepoRef RepoRef = new("octo", "app");

    private int _nextSpecIssue = 100;

    public IntegrationFixture()
    {
        Pulls = new InMemoryPullsAndStacks(branch => Git.RemoteTip(branch) ?? throw new InvalidOperationException($"'{branch}' was not pushed."));
        JournaledPulls = new JournalingPullsAndStacks(Pulls, Journal);
        Repository = RepositoryRecord.Register(RepoRef, Trunk, "https://github.com/octo/app.git", $"{WorkspaceRoot}/repos/octo/app", T0);
        Repository.SetEnabled(true, T0);
        Store.Add(Repository);
        TrunkTip = Git.SeedRemoteBranch(Trunk, "README.md");
    }

    public InMemoryWorkflowStore Store { get; } = new();

    public InMemoryGitWorkspace Git { get; } = new();

    public InMemoryPullsAndStacks Pulls { get; }

    public JournalingPullsAndStacks JournaledPulls { get; }

    public InMemoryGitHubIssues Issues { get; } = new();

    public ScriptedAgentRunner Agents { get; } = new();

    public ExternalCallJournal Journal { get; } = new();

    public FakeClock Clock { get; } = new(T0);

    public SequentialIdGenerator Ids { get; } = new();

    public FixedSettingsProvider Settings { get; } = new(WorkspaceRoot);

    public RepositoryIntegrationGate Gate { get; } = new();

    public RepositoryRecord Repository { get; }

    public GitRepositoryLocation Location => GitRepositoryLocation.From(Repository);

    public CommitSha TrunkTip { get; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <param name="unitOfWork">Defaults to a non-crashing wrapper around the store that still honours a crashed journal.</param>
    public IntegrationSagaRunner Runner(IUnitOfWork? unitOfWork = null)
    {
        unitOfWork ??= new CrashingUnitOfWork(Store, Journal);
        var git = new JournalingGitWorkspace(Git, Journal);
        var issues = new JournalingGitHubIssues(Issues, Journal);
        var conflicts = new ConflictResolutionRunner(
            Store, Store, git, Agents, new PromptRenderer(), Store, unitOfWork, Ids, Clock, new IntegrationOptions(SkillsRoot));
        var steps = new IntegrationSagaSteps(Store, Store, git, JournaledPulls, issues, conflicts, Store, unitOfWork, Clock);
        return new IntegrationSagaRunner(Store, Store, Store, Store, Store, Settings, Gate, steps, Store, unitOfWork, Clock);
    }

    /// <summary>A running spec whose run-scoped integration branch starts at <paramref name="baseSha"/> (default: trunk tip).</summary>
    public SpecRun SeedRunningSpec(CommitSha? baseSha = null, SpecDependencyMode? mode = null)
    {
        CommitSha start = baseSha ?? TrunkTip;
        int issueNumber = ++_nextSpecIssue;
        SpecRun spec = SpecRun.Queue(Ids.NewRunId(), Repository.Id, new IssueRef(RepoRef.Owner, RepoRef.Name, issueNumber), $"Spec {issueNumber}", "spec body", issueNumber, T0);
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.MaxActiveSpecsSlot = 1;
        spec.BaseBranch = Trunk;
        spec.DependencyModeUsed = mode;
        spec.IntegrationBaseSha = start;
        spec.IntegrationTipSha = start;
        spec.TransitionTo(SpecRunStatus.Running, T0);
        Git.UpdateBranchAsync(Location, spec.IntegrationBranch, start, null, Token).GetAwaiter().GetResult();
        Store.Add(spec);
        return spec;
    }

    /// <summary>
    /// A reviewed ticket: its worktree branched from the spec's current integration tip and its implementation commit
    /// touches <paramref name="files"/>. The ticket is left in <c>Reviewing</c>; <see cref="IntegrateAsync"/> moves it on.
    /// </summary>
    public TicketRun SeedReviewedTicket(SpecRun spec, int issueNumber, params string[] files)
    {
        var issue = new IssueRef(RepoRef.Owner, RepoRef.Name, issueNumber);
        Issues.Seed(issue, $"Ticket {issueNumber}", spec.ParentIssue);
        TicketRun ticket = TicketRun.Create(Ids.NewTicketRunId(), spec.Id, issue, $"Ticket {issueNumber}", $"Implement {issueNumber}.", T0);
        string worktreePath = TicketWorktreeLayout.PathFor(WorkspaceRoot, spec.Id, ticket.Id);
        Git.PrepareWorktreeAsync(Location, new WorktreeSpec(ticket.BranchName, spec.IntegrationTipSha!.Value, worktreePath), Token).GetAwaiter().GetResult();
        ticket.WorktreePath = worktreePath;
        ticket.LastImplementedSha = Git.CommitInWorktree(worktreePath, files);
        ticket.TransitionTo(TicketRunStatus.Ready, T0);
        ticket.TransitionTo(TicketRunStatus.Implementing, T0);
        ticket.TransitionTo(TicketRunStatus.Reviewing, T0);
        Store.Add(ticket);
        return ticket;
    }

    public static void MoveToIntegrating(TicketRun ticket)
    {
        if (ticket.Status == TicketRunStatus.Reviewing)
        {
            ticket.TransitionTo(TicketRunStatus.Integrating, T0);
        }
    }

    public IntegrationAssignment AssignmentFor(TicketRun ticket) => new(Repository.Id, ticket.SpecRunId, ticket.Id);

    public Task<IntegrationResult> IntegrateAsync(TicketRun ticket, IUnitOfWork? unitOfWork = null)
    {
        MoveToIntegrating(ticket);
        return Runner(unitOfWork).RunAsync(AssignmentFor(ticket), Token);
    }

    /// <summary>A conflict resolver turn that merges the integration tip into the ticket worktree and reports the merge commit.</summary>
    public void ScriptResolver(SpecRun spec)
    {
        Agents.Script(AgentRole.ConflictResolver, request =>
        {
            string path = request.Policy.Paths.WorkingDirectory;
            WorktreeInspection worktree = Git.InspectWorktreeAsync(Location, path, Token).GetAwaiter().GetResult();
            CommitSha tip = Git.GetBranchTipAsync(Location, spec.IntegrationBranch, GitRefScope.Local, Token).GetAwaiter().GetResult()!.Value;
            GitMergeResult merged = Git.MergeIntoWorktreeAsync(
                new TicketWorktree(path, worktree.Branch!.Value, worktree.Head!.Value), tip, "Merge integration tip", Token).GetAwaiter().GetResult();
            return new ConflictResolutionReport(ConflictResolutionStatus.Resolved, merged.Commit, ["shared.cs"], "Kept both sides.", []);
        });
    }

    public void UseTemplate(AgentRole role, string template) =>
        Settings.Defaults = Settings.Defaults with
        {
            Roles = Settings.Defaults.Roles.ToDictionary(pair => pair.Key, pair => pair.Key == role ? pair.Value with { PromptTemplate = template } : pair.Value),
        };

    public IReadOnlyList<PullStackLayer> Layers(SpecRun spec) =>
        ((IPullStackLayerRepository)Store).ListBySpecRunAsync(spec.Id, Token).GetAwaiter().GetResult();

    public IntegrationSaga? Saga(TicketRun ticket) => Store.FindLatestForTicketAsync(ticket.Id, Token).GetAwaiter().GetResult();

    public CommitSha LocalTip(BranchName branch) => Git.GetBranchTipAsync(Location, branch, GitRefScope.Local, Token).GetAwaiter().GetResult()!.Value;

    public IReadOnlyList<string> ChangedFiles(CommitSha from, CommitSha to) => Git.GetChangedFilesAsync(Location, from, to, Token).GetAwaiter().GetResult();

    public bool IsAncestor(CommitSha ancestor, CommitSha descendant) => Git.IsAncestorAsync(Location, ancestor, descendant, Token).GetAwaiter().GetResult();

    public IssueState IssueState(TicketRun ticket) => Issues.GetIssueAsync(ticket.Issue, Token).GetAwaiter().GetResult().State;

    public IEnumerable<TicketRunStatusChanged> Transitions(TicketRun ticket, TicketRunStatus to) =>
        Store.PendingEvents.OfType<TicketRunStatusChanged>().Where(changed => changed.TicketRunId == ticket.Id && changed.To == to);

    public IEnumerable<string> CallsOf(string prefix) => Journal.Calls.Where(call => call.StartsWith(prefix, StringComparison.Ordinal));
}
