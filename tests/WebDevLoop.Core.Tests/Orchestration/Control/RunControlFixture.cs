using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.Completion.Testing;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Orchestration.Control;

/// <summary>Wires <see cref="RunControlService"/> to the in-memory workflow store, a scripted agent runner, and a fake tester supervisor.</summary>
internal sealed class RunControlFixture : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    public static readonly GitHubRepoRef Repo = new("octo", "app");
    public static readonly CommitSha Head = new("1111111111111111111111111111111111111111");
    public static readonly CommitSha Tip = new("2222222222222222222222222222222222222222");

    private int _nextTicket;
    private int _nextStep;
    private int _nextSpec;

    public RunControlFixture(int maxActiveSpecs = 1)
    {
        GlobalSettings = SettingsProfile.ForGlobal();
        GlobalSettings.WorkspaceRootDirectory = "/work";
        GlobalSettings.MaxActiveSpecsPerRepo = maxActiveSpecs;
        Store.Add(GlobalSettings);
        Repository = RepositoryRecord.Register(Repo, new BranchName("main"), "https://github.com/octo/app.git", "/work/repos/octo/app", T0);
        Repository.SetEnabled(true, T0);
        Store.Add(Repository);
    }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public InMemoryWorkflowStore Store { get; } = new();

    public FakeClock Clock { get; } = new(T0);

    public ScriptedAgentRunner Agents { get; } = new();

    public ScriptedTestTarget Targets { get; } = new();

    public RepositoryIntegrationGate Gate { get; } = new();

    public SettingsProfile GlobalSettings { get; }

    public RepositoryRecord Repository { get; }

    public RunControlOptions Options { get; set; } = RunControlOptions.Default;

    public IRunControl Control()
    {
        (SpecRunControl specs, TicketRunControl tickets) = Controls();
        return new RunControlService(specs, tickets);
    }

    public (SpecRunControl Specs, TicketRunControl Tickets) Controls()
    {
        var settings = new PersistedEffectiveSettingsProvider(Store, new SettingsResolver(TestSettings.EmbeddedDefaults()));
        var scheduler = new SpecQueueScheduler(Store, Store, new InMemoryGitHubIssues(), settings, Store, Store, Clock);
        var journal = new RunControlJournal(Store, Store, Clock);
        var stopper = new ActiveWorkStopper(Agents, Targets);
        return (
            new SpecRunControl(Store, Store, Store, Store, scheduler, Gate, stopper, journal, Store, Options),
            new TicketRunControl(Store, Store, Store, Store, Gate, stopper, journal, Store, Options));
    }

    /// <summary>Seeds a spec that walked <paramref name="path"/> from <c>Queued</c>; active phases hold <paramref name="slot"/>.</summary>
    public SpecRun SeedSpec(int slot = 1, params SpecRunStatus[] path)
    {
        SpecRun spec = SpecRun.Queue(new RunId($"run{++_nextSpec}"), Repository.Id, new IssueRef(Repo.Owner, Repo.Name, _nextSpec), $"Spec {_nextSpec}", "body", _nextSpec, T0);
        foreach (SpecRunStatus next in path)
        {
            if (next == SpecRunStatus.Preparing)
            {
                spec.MaxActiveSpecsSlot = slot;
                spec.BaseBranch = new BranchName("main");
                spec.IntegrationBaseSha = Tip;
                spec.IntegrationTipSha = Tip;
            }

            spec.TransitionTo(next, T0);
        }

        Store.Add(spec);
        return spec;
    }

    /// <summary>A spec that needs attention after failing in the last phase of <paramref name="path"/>.</summary>
    public SpecRun SeedFailedSpec(params SpecRunStatus[] path)
    {
        SpecRun spec = SeedSpec(1, path);
        spec.MarkNeedsAttention(AttentionReasons.Unclassified($"{path[^1]} failed", true), T0);
        return spec;
    }

    public TicketRun SeedTicket(SpecRun spec, params TicketRunStatus[] path)
    {
        TicketRun ticket = TicketRun.Create(new TicketRunId($"t{++_nextTicket}"), spec.Id, new IssueRef(Repo.Owner, Repo.Name, 100 + _nextTicket), $"Ticket {_nextTicket}", "body", T0);
        foreach (TicketRunStatus next in path)
        {
            ticket.TransitionTo(next, T0);
        }

        if (ticket.Status is not (TicketRunStatus.Blocked or TicketRunStatus.Ready or TicketRunStatus.Implementing))
        {
            ticket.LastImplementedSha = Head;
        }

        Store.Add(ticket);
        return ticket;
    }

    public TicketRun SeedFailedTicket(SpecRun spec, params TicketRunStatus[] path)
    {
        TicketRun ticket = SeedTicket(spec, path);
        ticket.MarkNeedsAttention(AttentionReasons.Unclassified($"{path[^1]} failed", true), T0);
        return ticket;
    }

    public void Block(TicketRun blocked, TicketRun blocking) =>
        Store.AddDependency(TicketDependency.Create(blocked.SpecRunId, blocked.Id, blocking.Id, DependencySource.GitHub));

    /// <summary>A running step; agent steps carry a Copilot session id.</summary>
    public StepRun SeedRunningStep(SpecRun spec, TicketRun? ticket, StepKind kind, AgentRole? role)
    {
        StepRun step = StepRun.Create(new StepRunId($"s{++_nextStep}"), spec.Id, ticket?.Id, kind, role, 1, "hash");
        if (role is not null)
        {
            step.CopilotSessionId = $"session-{step.Id}";
        }

        step.Start(T0, TimeSpan.FromHours(1));
        Store.Add(step);
        return step;
    }

    public TestLease SeedLease(SpecRun spec, int port)
    {
        TestLease lease = TestLease.Acquire(spec.Id, port, "/work/runs/test", T0, TimeSpan.FromHours(1));
        Store.Add(lease);
        return lease;
    }

    public IntegrationSaga SeedSaga(TicketRun ticket, IntegrationSagaCheckpoint checkpoint)
    {
        IntegrationSaga saga = IntegrationSaga.Start(ticket.SpecRunId, ticket.Id, Tip, T0);
        if (checkpoint != IntegrationSagaCheckpoint.Started)
        {
            saga.AdvanceTo(checkpoint, T0);
        }

        Store.Add(saga);
        return saga;
    }

    public IReadOnlyList<WorkflowEvent> Events => Store.PendingEvents.ToArray();

    public IReadOnlyList<RunEvent> AuditOf(SpecRun spec) =>
        ((IRunEventRepository)Store).ListBySpecRunAsync(spec.Id, Token).GetAwaiter().GetResult();

    public IReadOnlyList<AgentSessionId> AbortedSessions => Agents.Aborted;

    public void Dispose() => Gate.Dispose();
}
