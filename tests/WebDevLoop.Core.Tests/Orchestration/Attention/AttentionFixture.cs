using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Orchestration.Attention;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.Control;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Orchestration.Attention;

/// <summary>Wires the resolution pipeline (known remediation, then any extra stage) to the in-memory store and git fakes.</summary>
internal sealed class AttentionFixture : IDisposable
{
    public static readonly SpecRunStatus[] ToRunning = [SpecRunStatus.Preparing, SpecRunStatus.Running];
    public static readonly TicketRunStatus[] ToReviewing = [TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing];
    public static readonly TicketRunStatus[] ToIntegrating = [.. ToReviewing, TicketRunStatus.Integrating];

    public RunControlFixture Run { get; } = new(maxActiveSpecs: 2);

    public InMemoryGitWorkspace Git { get; } = new();

    /// <summary>The pauses the transient-failure remediation asked for.</summary>
    public List<TimeSpan> Pauses { get; } = [];

    public CommitSha Base { get; private set; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A real directory for the files the troubleshooter writes (context, backups); only created when a test asks for the troubleshooter.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "wdl-troubleshooter-" + Guid.NewGuid().ToString("N"));

    public ScriptedAgentRunner Agents => Run.Agents;

    private readonly SequentialIdGenerator _ids = new();

    public List<AgentLogView> Logs { get; } = [];

    public void Dispose()
    {
        Run.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>The ticket worktree path once <see cref="Troubleshooter"/> moved the workspace root to <see cref="Root"/>.</summary>
    public string TicketPath(SpecRun spec, TicketRun ticket) => TicketWorktreeLayout.PathFor(Root, spec.Id, ticket.Id);

    public RunWorkspaceLayout LayoutOf(SpecRun spec) => RunWorkspaceLayout.For(Root, spec.Id);

    /// <summary>The troubleshooter stage wired to the fakes, rendering the shipped prompt template. Moves the workspace root to <see cref="Root"/>.</summary>
    public IAttentionStage Troubleshooter()
    {
        Run.GlobalSettings.WorkspaceRootDirectory = Root;
        Run.GlobalSettings.SetRole(AgentRole.Troubleshooter, new RoleSettingsOverride(PromptTemplate: ShippedTemplate()));
        var settings = new PersistedEffectiveSettingsProvider(Run.Store, new SettingsResolver(TestSettings.EmbeddedDefaults()));
        var loader = new AttentionWorkLoader(Run.Store, Run.Store, Run.Store, settings);
        var pulls = new InMemoryPullsAndStacks(_ => Base);
        var options = new TroubleshooterOptions("/skills");
        return new TroubleshooterStage(
            loader,
            new TroubleshootingStateReader(Git, pulls, Run.Store),
            new TroubleshooterWorkspace(Git, Run.Store, new FixedLogReader(Logs), Run.Clock, options),
            new TroubleshooterVerifier(Git),
            Agents,
            new PromptRenderer(),
            Run.Store,
            Run.Store,
            Run.Store,
            Run.Store,
            Run.Store,
            _ids,
            Run.Clock,
            options);
    }

    public static string ShippedTemplate()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "WebDevLoop.slnx")))
        {
            directory = Path.GetDirectoryName(directory) ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        return File.ReadAllText(Path.Combine(directory, "src", "WebDevLoop.Web", "Resources", "Prompts", "Troubleshooter.md"));
    }

    private sealed class FixedLogReader(List<AgentLogView> entries) : IAgentLogReader
    {
        public Task<IReadOnlyList<AgentLogView>> ReadAsync(StepRunId stepRunId, int afterSequence, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentLogView>>(entries);
    }

    /// <summary>A spec that reached <c>Running</c> on an integration branch at a base commit; the branch exists locally.</summary>
    public SpecRun SeedRunningSpec()
    {
        SpecRun spec = Run.SeedSpec(1, ToRunning);
        Base = Git.Commit([], "README.md");
        spec.IntegrationBaseSha = Base;
        spec.IntegrationTipSha = Base;
        Git.UpdateBranchAsync(Location, spec.IntegrationBranch, Base, null, Token).GetAwaiter().GetResult();
        return spec;
    }

    public GitRepositoryLocation Location => GitRepositoryLocation.From(Run.Repository);

    public static string WorktreeOf(SpecRun spec, TicketRun ticket) => TicketWorktreeLayout.PathFor("/work", spec.Id, ticket.Id);

    /// <summary>A ticket that failed in the last status of <paramref name="path"/> with <paramref name="reason"/>.</summary>
    public TicketRun SeedParkedTicket(SpecRun spec, AttentionReason reason, CommitSha? implemented, params TicketRunStatus[] path)
    {
        TicketRun ticket = Run.SeedTicket(spec, path);
        ticket.LastImplementedSha = implemented;
        ticket.MarkNeedsAttention(reason, RunControlFixture.T0);
        return ticket;
    }

    public AttentionTriageService Triage(params IAttentionStage[] laterStages)
    {
        var settings = new PersistedEffectiveSettingsProvider(Run.Store, new SettingsResolver(TestSettings.EmbeddedDefaults()));
        var loader = new AttentionWorkLoader(Run.Store, Run.Store, Run.Store, settings);
        var options = new AttentionTriageOptions(
            [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)],
            (pause, _) =>
            {
                Pauses.Add(pause);
                return Task.CompletedTask;
            });
        IKnownRemediation[] known =
        [
            new WorktreeNotCleanRemediation(loader, new WorktreeRemediator(Git, Run.Store, Run.Clock), Git),
            new TicketBranchRebaseRemediation(loader, Git),
            new TransientFailureRemediation(options),
            new StaleIntegrationBranchRemediation(loader, Run.Store, Run.Store, Git),
            new ExplorationRetryRemediation(),
            new NoChangesTicketRemediation(loader, Run.Store),
        ];
        (SpecRunControl specs, TicketRunControl tickets) = Run.Controls();
        IAttentionStage[] stages = [new KnownRemediationStage(known, Run.Store, Run.Clock), .. laterStages];
        return new AttentionTriageService(stages, Run.Store, Run.Store, Run.Store, specs, tickets, Run.Store, Run.Clock);
    }

    public IReadOnlyList<RunEvent> EventsOf(SpecRun spec) => Run.AuditOf(spec);
}
