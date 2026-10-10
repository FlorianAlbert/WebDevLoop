using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.Completion.Testing;
using WebDevLoop.Core.Tests.Orchestration.Preparation;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// One repository driven through the real queue, preparation, integration saga (draft PR stack), and tester runner against
/// in-memory Git, issues, and PRs, so completion and merge tracking see exactly what earlier workflow steps persisted.
/// Completion runs whenever <see cref="DeliverEventsAsync"/> hands outbox events to the completion handler.
/// </summary>
internal sealed class ReadyAndMergeFixture
{
    public const string WorkspaceRoot = "/work";
    public const string SkillsRoot = "/app/skills";

    private const string SpecRunIdProperty = "SpecRunId";
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    public static readonly GitHubRepoRef Repo = new("octo", "app");
    public static readonly BranchName Trunk = new("main");

    private readonly HashSet<int> _seededSpecs = [];
    private readonly List<WorkflowEvent> _delivered = [];

    public ReadyAndMergeFixture()
    {
        GlobalSettings = SettingsProfile.ForGlobal();
        GlobalSettings.WorkspaceRootDirectory = WorkspaceRoot;
        GlobalSettings.BaseBranch = Trunk;
        Store.Add(GlobalSettings);

        Repository = RepositoryRecord.Register(Repo, Trunk, "https://github.com/octo/app.git", "/work/repos/octo/app", T0);
        Repository.SetEnabled(true, T0);
        Store.Add(Repository);

        Git.SeedRemoteBranch(Trunk, "README.md");
        Pulls = new StackPullsFake(Git, Location, Trunk);
        Settings = new PersistedEffectiveSettingsProvider(Store, new SettingsResolver(TestSettings.EmbeddedDefaults()));
    }

    public InMemoryWorkflowStore Store { get; } = new();

    public InMemoryGitWorkspace Git { get; } = new();

    public InMemoryGitHubIssues Issues { get; } = new();

    public StackPullsFake Pulls { get; }

    public FakeClock Clock { get; } = new(T0);

    public SequentialIdGenerator Ids { get; } = new();

    public TesterAgentStub Tester { get; } = new();

    public ScriptedTestTarget Target { get; } = new();

    public RecordingCompletionLauncher Launcher { get; } = new();

    public RepositoryIntegrationGate Gate { get; } = new();

    public ReadyAndMergeOptions Options { get; set; } = new();

    public IEffectiveSettingsProvider Settings { get; }

    public SettingsProfile GlobalSettings { get; }

    public RepositoryRecord Repository { get; }

    public GitRepositoryLocation Location => GitRepositoryLocation.From(Repository);

    /// <summary>Results of completions run for launched assignments, in launch order.</summary>
    public List<(CompletionAssignment Assignment, CompletionResult Result)> Completions { get; } = [];

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Every workflow event saved so far, delivered or not.</summary>
    public IEnumerable<WorkflowEvent> Events => _delivered.Concat(Store.PendingEvents);

    public SpecCompletionService Completion() =>
        new(Store, Store, Store, Store, Store, Store, Git, Pulls, Issues, Gate, Store, Store, Clock);

    public MergeTrackingService Tracking() => new(Store, Store, Store, Store, Pulls, Completion(), Store, Store, Clock, Options);

    public CompletionEventHandler Handler() => new(Launcher);

    public void ConfigureQueue(int maxActiveSpecs, SpecDependencyMode mode = SpecDependencyMode.WaitForMerge)
    {
        GlobalSettings.MaxActiveSpecsPerRepo = maxActiveSpecs;
        GlobalSettings.SpecDependencyMode = mode;
    }

    public void SeedSpec(int number, params int[] blockedBySpecs)
    {
        Issues.Seed(Issue(number), $"Spec {number}", null, blockedBySpecs.Select(Issue).ToArray());
        _seededSpecs.Add(number);
    }

    public async Task<SpecRun> EnqueueAsync(int specNumber, params int[] tickets)
    {
        if (!_seededSpecs.Contains(specNumber))
        {
            SeedSpec(specNumber);
        }

        foreach (int ticket in tickets)
        {
            Issues.Seed(Issue(ticket), $"Ticket {ticket}", Issue(specNumber));
        }

        var queue = new SpecQueueService(Store, Store, Issues, Settings, Store, Store, Ids, Clock);
        EnqueueResult result = await queue.EnqueueAsync(Repository.Id, specNumber, Token);
        Assert.Equal(EnqueueOutcome.Queued, result.Outcome);
        return Spec(result.SpecRunId!.Value);
    }

    public Task<SpecScheduleResult> ScheduleAsync() =>
        new SpecQueueScheduler(Store, Store, Issues, Settings, Store, Store, Clock).ScheduleRepositoryAsync(Repository.Id, Token);

    public async Task PrepareAsync(SpecRun spec)
    {
        var options = new SpecPreparationOptions(SkillsRoot, ExplorationEnabled: false);
        var agents = new ScriptedAgentRunner();
        var explorer = new SpecExplorer(Store, Store, Git, agents, new RecordingDirectoryProvisioner(), new PromptRenderer(), Store, Store, Ids, Clock, options);
        var preparation = new SpecPreparationService(
            Store, Store, Settings, Git, new SpecSnapshotter(Issues, Store, Ids, Clock), explorer, Store, Store, Clock, options);
        Assert.Equal(PreparationOutcome.Prepared, await preparation.PrepareAsync(spec.Id, Token));
        Assert.Equal(SpecRunStatus.Running, spec.Status);
    }

    /// <summary>Enqueues the spec with its tickets, claims the free slot, and prepares it: <c>Running</c>.</summary>
    public async Task<SpecRun> StartAsync(int specNumber, params int[] tickets)
    {
        SpecRun spec = await EnqueueAsync(specNumber, tickets);
        await ScheduleAsync();
        Assert.Equal(SpecRunStatus.Preparing, spec.Status);
        await PrepareAsync(spec);
        return spec;
    }

    /// <summary>Implements a ticket in its own worktree and runs the real integration saga, which publishes a draft PR layer.</summary>
    public async Task IntegrateAsync(SpecRun spec, int ticketNumber)
    {
        TicketRun ticket = await ImplementAsync(spec, ticketNumber);
        ticket.TransitionTo(TicketRunStatus.Integrating, Clock.UtcNow);
        var conflicts = new ConflictResolutionRunner(
            Store, Store, Store, Git, new ScriptedAgentRunner(), new PromptRenderer(), Store, Store, Ids, Clock, new IntegrationOptions(SkillsRoot));
        var steps = new IntegrationSagaSteps(Store, Store, Store, Git, Pulls, Issues, conflicts, Store, Store, Clock);
        var saga = new IntegrationSagaRunner(Store, Store, Store, Store, Store, Settings, Gate, steps, Store, Store, Clock);

        IntegrationResult result = await saga.RunAsync(new IntegrationAssignment(Repository.Id, spec.Id, ticket.Id), Token);

        Assert.Equal(IntegrationOutcome.Integrated, result.Outcome);
    }

    /// <summary>Squash-merges a ticket onto the integration branch without publishing a PR; its issue stays open.</summary>
    public async Task IntegrateWithoutPullRequestAsync(SpecRun spec, int ticketNumber)
    {
        TicketRun ticket = await ImplementAsync(spec, ticketNumber);
        CommitSha tip = spec.IntegrationTipSha!.Value;
        CommitSha squash = Git.Commit([tip], $"ticket{ticketNumber}.cs");
        await Git.UpdateBranchAsync(Location, spec.IntegrationBranch, squash, tip, Token);
        spec.IntegrationTipSha = squash;
        ticket.IntegratedCommitSha = squash;
        ticket.TransitionTo(TicketRunStatus.Integrating, Clock.UtcNow);
        ticket.TransitionTo(TicketRunStatus.Integrated, Clock.UtcNow);
        await SaveAsync();
    }

    public async Task MoveAsync(SpecRun spec, params SpecRunStatus[] path)
    {
        foreach (SpecRunStatus next in path)
        {
            SpecRunStatus previous = spec.Status;
            spec.TransitionTo(next, Clock.UtcNow);
            Store.Append(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, previous, next, Clock.UtcNow));
        }

        await SaveAsync();
    }

    /// <summary>Runs the real tester runner with a passing tester: the spec stays <c>Testing</c> and <see cref="SpecTestingPassed"/> is saved.</summary>
    public async Task PassTestsAsync(SpecRun spec)
    {
        Tester.Reports(TestingFixture.Pass());
        var issuer = new FindingTicketIssuer(Store, Store, Issues, Store, Ids, Clock);
        var attempts = new TesterAttemptRunner(Store, Store, Tester, Target, Git, new PromptRenderer(), Store, Store, Ids, Clock, new TestingOptions(SkillsRoot));
        var runner = new SpecTestRunner(Store, Store, Store, Store, Settings, Git, attempts, issuer, Store, Store, Clock);

        TestingResult result = await runner.RunAsync(new TestingAssignment(spec.Id), Token);

        Assert.Equal(TestingOutcome.Passed, result.Outcome);
    }

    /// <summary>A spec whose tickets were integrated as a draft PR stack, that passed the parent review, and whose tester passed.</summary>
    public async Task<SpecRun> TestedSpecAsync(int specNumber, params int[] tickets)
    {
        SpecRun spec = await StartAsync(specNumber, tickets);
        foreach (int ticket in tickets)
        {
            await IntegrateAsync(spec, ticket);
        }

        await MoveAsync(spec, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);
        await PassTestsAsync(spec);
        return spec;
    }

    /// <summary>A tested spec that completion moved to <c>AwaitingMerge</c>.</summary>
    public async Task<SpecRun> AwaitingMergeSpecAsync(int specNumber, params int[] tickets)
    {
        SpecRun spec = await TestedSpecAsync(specNumber, tickets);
        await DeliverEventsAsync();
        Assert.Equal(SpecRunStatus.AwaitingMerge, spec.Status);
        return spec;
    }

    /// <summary>
    /// Outbox dispatcher: hands every saved event to the completion handler, then runs the launched completions (each in
    /// its own service instance), until no new events appear.
    /// </summary>
    public async Task DeliverEventsAsync()
    {
        const int MaxRounds = 20;
        for (int round = 0; round < MaxRounds; round++)
        {
            IReadOnlyList<EventEnvelope> pending = await Store.ReadPendingAsync(int.MaxValue, Token);
            if (pending.Count == 0)
            {
                return;
            }

            foreach (EventEnvelope envelope in pending)
            {
                await Handler().HandleAsync(envelope, Token);
                await Store.MarkDispatchedAsync(envelope.MessageId, Token);
                _delivered.Add(envelope.Event);
            }

            while (Launcher.TryTake(out CompletionAssignment? assignment))
            {
                Completions.Add((assignment, await Completion().RunAsync(assignment, Token)));
            }
        }

        throw new InvalidOperationException("Event delivery did not settle.");
    }

    /// <summary>A human merges every PR of the stack and trunk receives the top layer.</summary>
    public async Task<CommitSha> MergeStackAsync(SpecRun spec)
    {
        IReadOnlyList<PullStackLayer> layers = Layers(spec);
        Pulls.Merge(layers.Select(layer => layer.PullRequestNumber).ToArray());
        CommitSha previousTrunk = Git.RemoteTip(Trunk)!.Value;
        CommitSha merged = Git.Commit([previousTrunk, layers[^1].CommitSha]);
        Assert.Equal(PushOutcome.Pushed, await Git.PushAsync(Location, new RefPush(Trunk, merged, previousTrunk), Token));
        return merged;
    }

    public void MergePullRequestsOnly(SpecRun spec) => Pulls.Merge(Layers(spec).Select(layer => layer.PullRequestNumber).ToArray());

    public IReadOnlyList<PullStackLayer> Layers(SpecRun spec) =>
        ((IPullStackLayerRepository)Store).ListBySpecRunAsync(spec.Id, Token).GetAwaiter().GetResult();

    public SpecRun Spec(RunId id) => ((ISpecRunRepository)Store).GetAsync(id, Token).GetAwaiter().GetResult()!;

    public TicketRun Ticket(SpecRun spec, int issueNumber) =>
        ((ITicketRunRepository)Store).ListBySpecRunAsync(spec.Id, Token).GetAwaiter().GetResult().Single(ticket => ticket.Issue.Number == issueNumber);

    public IReadOnlyList<RunEvent> RunEvents(SpecRun spec) =>
        ((IRunEventRepository)Store).ListBySpecRunAsync(spec.Id, Token).GetAwaiter().GetResult();

    public bool WorktreeExists(string path) => Git.InspectWorktreeAsync(Location, path, Token).GetAwaiter().GetResult().Status != WorktreeStatus.Missing;

    public IssueState IssueState(int number) => Issues.GetIssueAsync(Issue(number), Token).GetAwaiter().GetResult().State;

    public IEnumerable<(SpecRunStatus From, SpecRunStatus To)> Transitions(SpecRun spec) =>
        Events.OfType<SpecRunStatusChanged>().Where(changed => changed.SpecRunId == spec.Id).Select(changed => (changed.From, changed.To));

    public IEnumerable<T> EventsOf<T>(SpecRun spec) where T : WorkflowEvent =>
        Events.OfType<T>().Where(workflowEvent => typeof(T).GetProperty(SpecRunIdProperty)!.GetValue(workflowEvent) is RunId id && id == spec.Id);

    public async Task SaveAsync() => Assert.Equal(SaveOutcome.Saved, await Store.SaveChangesAsync(Token));

    public static IssueRef Issue(int number) => new(Repo.Owner, Repo.Name, number);

    private async Task<TicketRun> ImplementAsync(SpecRun spec, int ticketNumber)
    {
        TicketRun ticket = Ticket(spec, ticketNumber);
        if (ticket.Status == TicketRunStatus.Blocked)
        {
            ticket.TransitionTo(TicketRunStatus.Ready, Clock.UtcNow);
        }

        ticket.TransitionTo(TicketRunStatus.Implementing, Clock.UtcNow);
        string path = TicketWorktreeLayout.PathFor(WorkspaceRoot, spec.Id, ticket.Id);
        await Git.PrepareWorktreeAsync(Location, new WorktreeSpec(ticket.BranchName, spec.IntegrationTipSha!.Value, path), Token);
        ticket.WorktreePath = path;
        ticket.LastImplementedSha = Git.CommitInWorktree(path, $"ticket{ticketNumber}.cs");
        ticket.TransitionTo(TicketRunStatus.Reviewing, Clock.UtcNow);
        return ticket;
    }
}

internal sealed class RecordingCompletionLauncher : ICompletionLauncher
{
    private readonly Queue<CompletionAssignment> _pending = new();

    public List<CompletionAssignment> Launched { get; } = [];

    public void Launch(CompletionAssignment assignment)
    {
        Launched.Add(assignment);
        _pending.Enqueue(assignment);
    }

    public bool TryTake([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CompletionAssignment? assignment) => _pending.TryDequeue(out assignment);
}
