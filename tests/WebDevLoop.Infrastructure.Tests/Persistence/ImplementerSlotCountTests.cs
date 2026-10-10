using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

/// <summary>
/// Implementer slot accounting against SQLite: a review loop keeps one EF Core scope (and identity map) for its whole run,
/// so slots must be counted from committed rows, not from entities the scope tracked earlier.
/// </summary>
public sealed class ImplementerSlotCountTests : IDisposable
{
    private const int PerRepositoryLimit = 2;
    private const int GlobalLimit = 8;

    private readonly PersistenceHarness _harness = new();
    private int _nextIssueNumber = 100;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task capacity_in_a_long_lived_scope_sees_a_slot_freed_by_another_scope()
    {
        (int repositoryId, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);
        await SeedTicketsAsync(specRunId, ("t-1", TicketRunStatus.Implementing), ("t-2", TicketRunStatus.Implementing));
        using PersistenceScope loop = _harness.OpenScope();
        var capacity = new ImplementerCapacity(loop.Tickets);
        Assert.Equal(0, await capacity.GetAvailableAsync(repositoryId, Limits(), Token));
        await loop.Tickets.ListBySpecRunAsync(specRunId, Token);

        using (PersistenceScope other = _harness.OpenScope())
        {
            TicketRun finished = (await other.Tickets.GetAsync(new TicketRunId("t-1"), Token))!;
            finished.TransitionTo(TicketRunStatus.Reviewing, TestData.Now);
            await other.SaveAsync();
        }

        Assert.Equal(1, await capacity.GetAvailableAsync(repositoryId, Limits(), Token));
    }

    [Fact]
    public async Task occupied_slots_are_counted_globally_and_per_repository_for_non_terminal_runs_without_tracking()
    {
        (int alpha, RunId alphaRun) = await TestData.SeedSpecRunAsync(_harness, "run-alpha");
        (int beta, RunId betaRun) = await SeedSecondRepositoryRunAsync("run-beta");
        RunId abortedRun = await SeedSpecRunAsync(alpha, "run-aborted", aborted: true);
        await SeedTicketsAsync(alphaRun, ("a-1", TicketRunStatus.Implementing), ("a-2", TicketRunStatus.FixingReviewFindings), ("a-3", TicketRunStatus.Reviewing));
        await SeedTicketsAsync(betaRun, ("b-1", TicketRunStatus.Implementing));
        await SeedTicketsAsync(abortedRun, ("x-1", TicketRunStatus.Implementing));
        using PersistenceScope scope = _harness.OpenScope();

        ImplementerSlotUsage forAlpha = await scope.Tickets.CountOccupiedImplementerSlotsAsync(alpha, Token);
        ImplementerSlotUsage forBeta = await scope.Tickets.CountOccupiedImplementerSlotsAsync(beta, Token);

        Assert.Equal(new ImplementerSlotUsage(3, 2), forAlpha);
        Assert.Equal(new ImplementerSlotUsage(3, 1), forBeta);
        Assert.Empty(scope.Context.ChangeTracker.Entries());
    }

    private async Task<(int RepositoryId, RunId SpecRunId)> SeedSecondRepositoryRunAsync(string specRunId)
    {
        using PersistenceScope scope = _harness.OpenScope();
        RepositoryRecord repository = TestData.NewRepository("acme", "gadgets");
        scope.Repositories.Add(repository);
        await scope.SaveAsync();
        return (repository.Id, await SeedSpecRunAsync(repository.Id, specRunId, aborted: false));
    }

    private async Task<RunId> SeedSpecRunAsync(int repositoryId, string specRunId, bool aborted)
    {
        using PersistenceScope scope = _harness.OpenScope();
        SpecRun run = TestData.NewSpecRun(repositoryId, specRunId, issueNumber: ++_nextIssueNumber, queuePosition: _nextIssueNumber);
        if (aborted)
        {
            run.TransitionTo(SpecRunStatus.Aborted, TestData.Now);
        }

        scope.SpecRuns.Add(run);
        await scope.SaveAsync();
        return run.Id;
    }

    private async Task SeedTicketsAsync(RunId specRunId, params (string Id, TicketRunStatus Status)[] tickets)
    {
        using PersistenceScope scope = _harness.OpenScope();
        foreach ((string id, TicketRunStatus status) in tickets)
        {
            TicketRun ticket = TestData.NewTicketRun(specRunId, id, ++_nextIssueNumber);
            foreach (TicketRunStatus next in PathTo(status))
            {
                ticket.TransitionTo(next, TestData.Now);
            }

            scope.Tickets.Add(ticket);
        }

        await scope.SaveAsync();
    }

    private static TicketRunStatus[] PathTo(TicketRunStatus status) => status switch
    {
        TicketRunStatus.Implementing => [TicketRunStatus.Ready, TicketRunStatus.Implementing],
        TicketRunStatus.Reviewing => [TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing],
        TicketRunStatus.FixingReviewFindings => [TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.FixingReviewFindings],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No seeding path."),
    };

    private static EffectiveSettings Limits() => new()
    {
        WorkspaceRootDirectory = "/work",
        CopilotBaseDirectory = "/copilot",
        BaseBranch = new BranchName("main"),
        MaxActiveSpecsPerRepo = 1,
        SpecDependencyMode = SpecDependencyMode.WaitForMerge,
        MaxConcurrentImplementersGlobal = GlobalLimit,
        MaxConcurrentImplementersPerRepo = PerRepositoryLimit,
        MaxReviewIterations = 5,
        MaxRetries = 2,
        ParentReviewCycleLimit = 3,
        TesterCycleLimit = 3,
        TroubleshooterEnabled = true,
        TroubleshooterMaxAttempts = 2,
        TesterRunInstructions = "run",
        TestPortRange = new TestPortRange(41000, 41999),
        Roles = new Dictionary<AgentRole, RoleSettings>(),
    };
}
