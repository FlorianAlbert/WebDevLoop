using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.Preparation;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Orchestration.SpecQueue;

/// <summary>Wires the spec queue, scheduler, and preparation services to in-memory port fakes for one repository.</summary>
internal sealed class SpecWorkflowFixture
{
    public const string WorkspaceRoot = "/work";
    public const string RepositoryClonePath = "/work/repos/octo/app";
    public const string SkillsRoot = "/app/skills";

    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    public static readonly GitHubRepoRef Repo = new("octo", "app");
    public static readonly BranchName Trunk = new("main");

    public SpecWorkflowFixture(bool explorationEnabled = false)
    {
        GlobalSettings = SettingsProfile.ForGlobal();
        GlobalSettings.WorkspaceRootDirectory = WorkspaceRoot;
        GlobalSettings.BaseBranch = Trunk;
        Store.Add(GlobalSettings);

        Repository = RepositoryRecord.Register(Repo, Trunk, "https://github.com/octo/app.git", RepositoryClonePath, T0);
        Repository.SetEnabled(true, T0);
        Store.Add(Repository);

        TrunkTip = Git.SeedRemoteBranch(Trunk, "README.md");

        var settingsProvider = new PersistedEffectiveSettingsProvider(Store, new SettingsResolver(TestSettings.EmbeddedDefaults()));
        Queue = new SpecQueueService(Store, Store, Issues, settingsProvider, Store, Store, Ids, Clock);
        Scheduler = new SpecQueueScheduler(Store, Store, Issues, settingsProvider, Store, Store, Clock);
        var options = new SpecPreparationOptions(SkillsRoot, explorationEnabled);
        var explorer = new SpecExplorer(Store, Store, Git, Agents, Directories, new PromptRenderer(), Store, Store, Ids, Clock, options);
        Preparation = new SpecPreparationService(
            Store,
            Store,
            settingsProvider,
            Git,
            new SpecSnapshotter(Issues, Store, Ids, Clock),
            explorer,
            Store,
            Store,
            Clock,
            options);
    }

    public FakeClock Clock { get; } = new(T0);

    public SequentialIdGenerator Ids { get; } = new();

    public InMemoryGitHubIssues Issues { get; } = new();

    public InMemoryGitWorkspace Git { get; } = new();

    public InMemoryWorkflowStore Store { get; } = new();

    public ScriptedAgentRunner Agents { get; } = new();

    public RecordingDirectoryProvisioner Directories { get; } = new();

    public SettingsProfile GlobalSettings { get; }

    public RepositoryRecord Repository { get; }

    public CommitSha TrunkTip { get; }

    public SpecQueueService Queue { get; }

    public SpecQueueScheduler Scheduler { get; }

    public SpecPreparationService Preparation { get; }

    public static IssueRef Issue(int number) => new(Repo.Owner, Repo.Name, number);

    public void ConfigureQueue(int maxActiveSpecs, SpecDependencyMode mode = SpecDependencyMode.WaitForMerge)
    {
        GlobalSettings.MaxActiveSpecsPerRepo = maxActiveSpecs;
        GlobalSettings.SpecDependencyMode = mode;
    }

    public IssueSnapshot SeedSpec(int number, params int[] blockedBySpecs) =>
        Issues.Seed(Issue(number), $"Spec {number}", null, blockedBySpecs.Select(Issue).ToArray());

    public IssueSnapshot SeedTicket(int number, int spec, params int[] blockedByTickets) =>
        Issues.Seed(Issue(number), $"Ticket {number}", Issue(spec), blockedByTickets.Select(Issue).ToArray());

    public async Task<SpecRun> EnqueueAsync(int specNumber)
    {
        EnqueueResult result = await Queue.EnqueueAsync(Repository.Id, specNumber, TestContext.Current.CancellationToken);
        Assert.Equal(EnqueueOutcome.Queued, result.Outcome);
        return await RunAsync(result.SpecRunId!.Value);
    }

    public Task<SpecScheduleResult> ScheduleAsync() =>
        Scheduler.ScheduleRepositoryAsync(Repository.Id, TestContext.Current.CancellationToken);

    public Task<PreparationOutcome> PrepareAsync(SpecRun run) =>
        Preparation.PrepareAsync(run.Id, TestContext.Current.CancellationToken);

    public async Task<SpecRun> RunAsync(RunId id) =>
        await ((ISpecRunRepository)Store).GetAsync(id, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"Unknown run {id}.");

    /// <summary>Moves a run through later phases owned by subsequent orchestration WPs.</summary>
    public void Advance(SpecRun run, params SpecRunStatus[] path)
    {
        foreach (SpecRunStatus status in path)
        {
            run.TransitionTo(status, Clock.UtcNow);
        }
    }
}
