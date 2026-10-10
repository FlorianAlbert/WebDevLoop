using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Attention;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
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

    public void Dispose() => Run.Dispose();

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
