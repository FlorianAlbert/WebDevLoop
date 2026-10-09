using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

/// <summary>
/// Wires frontier and ticket execution to a compare-and-swap store, in-memory Git, and controllable agents. Every service
/// call gets a fresh unit-of-work scope, like a hosted worker handling one event or one background implementation.
/// </summary>
internal sealed class TicketExecutionFixture
{
    public const string WorkspaceRoot = "/work";
    public const string SkillsRoot = "/app/skills";

    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    public static readonly BranchName Trunk = new("main");

    private long _nextMessageId;

    public TicketExecutionFixture()
    {
        Settings = new FixedSettingsProvider(WorkspaceRoot);
        Settings.Defaults = Settings.Defaults with { MaxConcurrentImplementersGlobal = 8, MaxConcurrentImplementersPerRepo = 4 };
    }

    public CasWorkflowDatabase Db { get; } = new();

    public InMemoryGitWorkspace Git { get; } = new();

    public FakeClock Clock { get; } = new(T0);

    public SequentialIdGenerator Ids { get; } = new();

    public FixedSettingsProvider Settings { get; }

    public ImplementerCapacityGate Gate { get; } = new();

    public TestImplementationLauncher Launcher { get; } = new();

    public ImplementerAgentStub Agents { get; } = new();

    /// <summary>Results of runner executions started by <see cref="Launcher"/> after <see cref="UseRunner"/>.</summary>
    public List<ImplementationResult> Results { get; } = [];

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public FrontierService Frontier(CasWorkflowScope? scope = null, ImplementerCapacityGate? gate = null)
    {
        scope ??= Db.OpenScope();
        var dispatcher = new TicketDispatcher(
            Settings,
            new ImplementerCapacity(scope),
            gate ?? Gate,
            Launcher,
            scope,
            scope,
            Clock);
        return new FrontierService(scope, scope, dispatcher, scope, scope, Clock);
    }

    public TicketImplementationRunner Runner(CasWorkflowScope? scope = null)
    {
        scope ??= Db.OpenScope();
        return new TicketImplementationRunner(
            scope, scope, scope, scope, Settings, Git, Agents, new PromptRenderer(), scope, scope, Ids, Clock, new TicketExecutionOptions(SkillsRoot));
    }

    /// <summary>Launched assignments run the real implementation runner in their own scope, without being awaited.</summary>
    public void UseRunner() => Launcher.Run = async assignment => Results.Add(await Runner().RunAsync(assignment, Token));

    public void UseImplementerTemplate(string template) =>
        Settings.Defaults = Settings.Defaults with
        {
            Roles = Settings.Defaults.Roles.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == AgentRole.Implementer ? pair.Value with { PromptTemplate = template } : pair.Value),
        };

    public CommitSha CommitInWorktree(AgentRunRequest request, string file = "feature.cs") =>
        Git.CommitInWorktree(request.Policy.Paths.WorkingDirectory, file);

    /// <summary>What the implementer must do before reporting: merge the current integration tip into its branch.</summary>
    public async Task<CommitSha> MergeIntegrationTipAsync(AgentRunRequest request, SeededSpec spec)
    {
        string path = request.Policy.Paths.WorkingDirectory;
        CommitSha tip = (await Git.GetBranchTipAsync(spec.Location, spec.IntegrationBranch, GitRefScope.Local, Token))!.Value;
        WorktreeInspection worktree = await Git.InspectWorktreeAsync(spec.Location, path, Token);
        GitMergeResult merged = await Git.MergeIntoWorktreeAsync(new TicketWorktree(path, worktree.Branch!.Value, worktree.Head!.Value), tip, "Merge integration tip", Token);
        return merged.Commit!.Value;
    }

    public static AgentRunResult Completed(CommitSha head) => AgentRunResult.Reported(ImplementationReport.Completed(head, "Implemented with TDD."));

    public Task<FrontierResult> ReconcileAsync(RunId specRunId) => Frontier().ReconcileAsync(specRunId, Token);

    public Task HandleAsync(WorkflowEvent workflowEvent) =>
        new FrontierEventHandler(Frontier()).HandleAsync(new EventEnvelope(++_nextMessageId, workflowEvent), Token);

    /// <summary>Delivers committed events to the frontier handler until no new events appear (outbox dispatcher).</summary>
    public async Task PumpEventsAsync()
    {
        const int MaxRounds = 50;
        for (int round = 0; round < MaxRounds; round++)
        {
            IReadOnlyList<WorkflowEvent> pending = Db.TakeUndispatchedEvents();
            if (pending.Count == 0)
            {
                return;
            }

            foreach (WorkflowEvent workflowEvent in pending)
            {
                await HandleAsync(workflowEvent);
            }
        }

        throw new InvalidOperationException("Event pumping did not settle.");
    }

    public async Task<SeededSpec> SeedRunningSpecAsync(string repositoryName, params (int Number, int[] BlockedBy)[] tickets)
    {
        var repositoryRef = new GitHubRepoRef("octo", repositoryName);
        RepositoryRecord repository = RepositoryRecord.Register(
            repositoryRef, Trunk, $"https://github.com/octo/{repositoryName}.git", $"{WorkspaceRoot}/repos/octo/{repositoryName}", T0);
        repository.SetEnabled(true, T0);
        CasWorkflowScope scope = Db.OpenScope();
        ((IRepositoryRecordRepository)scope).Add(repository);
        await SaveAsync(scope);

        CommitSha baseSha = Git.SeedRemoteBranch(Trunk, $"{repositoryName}.md");
        SpecRun spec = SpecRun.Queue(Ids.NewRunId(), repository.Id, new IssueRef("octo", repositoryName, 1), "Spec", "spec body", 1, T0);
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.MaxActiveSpecsSlot = 1;
        spec.BaseBranch = Trunk;
        spec.IntegrationBaseSha = baseSha;
        spec.IntegrationTipSha = baseSha;
        spec.TransitionTo(SpecRunStatus.Running, T0);
        await Git.UpdateBranchAsync(GitRepositoryLocation.From(repository), spec.IntegrationBranch, baseSha, null, Token);
        scope.Add(spec);

        var ticketIds = new Dictionary<int, TicketRunId>();
        foreach ((int number, _) in tickets)
        {
            TicketRun ticket = TicketRun.Create(Ids.NewTicketRunId(), spec.Id, new IssueRef("octo", repositoryName, number), $"Ticket {number}", $"Implement {number}.", T0);
            scope.Add(ticket);
            ticketIds[number] = ticket.Id;
        }

        foreach ((int number, int[] blockedBy) in tickets)
        {
            foreach (int blocker in blockedBy)
            {
                scope.AddDependency(TicketDependency.Create(spec.Id, ticketIds[number], ticketIds[blocker], DependencySource.GitHub));
            }
        }

        await SaveAsync(scope);
        return new SeededSpec(spec.Id, repository.Id, GitRepositoryLocation.From(repository), spec.IntegrationBranch, ticketIds);
    }

    /// <summary>Moves a ticket along <paramref name="path"/> (phases owned by later WPs) with the matching events.</summary>
    public async Task MoveAsync(TicketRunId ticketId, params TicketRunStatus[] path)
    {
        CasWorkflowScope scope = Db.OpenScope();
        TicketRun ticket = (await scope.GetAsync(ticketId, Token))!;
        foreach (TicketRunStatus next in path)
        {
            TicketRunStatus previous = ticket.Status;
            ticket.TransitionTo(next, Clock.UtcNow);
            scope.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, next, Clock.UtcNow));
        }

        await SaveAsync(scope);
    }

    /// <summary>Simulates review and the integration saga: squash onto the integration tip, then Integrated.</summary>
    public async Task IntegrateAsync(SeededSpec spec, TicketRunId ticketId)
    {
        CasWorkflowScope scope = Db.OpenScope();
        SpecRun run = (await scope.GetAsync(spec.Id, Token))!;
        CommitSha tip = run.IntegrationTipSha!.Value;
        CommitSha squash = Git.Commit([tip], $"{ticketId}.cs");
        await Git.UpdateBranchAsync(spec.Location, spec.IntegrationBranch, squash, tip, Token);
        run.IntegrationTipSha = squash;
        await SaveAsync(scope);

        TicketRunStatus status = Ticket(ticketId).Status;
        TicketRunStatus[] remaining = status == TicketRunStatus.Implementing
            ? [TicketRunStatus.Reviewing, TicketRunStatus.Integrating, TicketRunStatus.Integrated]
            : [TicketRunStatus.Integrating, TicketRunStatus.Integrated];
        await MoveAsync(ticketId, remaining);
    }

    public TicketRun Ticket(TicketRunId id) => Db.Rows<TicketRun>().Single(ticket => ticket.Id == id);

    public SpecRun Spec(RunId id) => Db.Rows<SpecRun>().Single(run => run.Id == id);

    public IReadOnlyList<StepRun> Steps(TicketRunId id) => Db.Rows<StepRun>().Where(step => step.TicketRunId == id).OrderBy(step => step.Attempt).ToArray();

    private static async Task SaveAsync(IUnitOfWork scope) =>
        Assert.Equal(SaveOutcome.Saved, await scope.SaveChangesAsync(Token));
}

internal sealed record SeededSpec(
    RunId Id,
    int RepositoryId,
    GitRepositoryLocation Location,
    BranchName IntegrationBranch,
    IReadOnlyDictionary<int, TicketRunId> Tickets)
{
    public TicketRunId this[int issueNumber] => Tickets[issueNumber];
}
