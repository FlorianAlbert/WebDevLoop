using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

public sealed class TicketDispatcherTests
{
    private readonly TicketExecutionFixture _fixture = new();

    [Fact]
    public async Task claim_moves_the_ticket_to_implementing_with_a_run_scoped_worktree_path()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (7, []));

        await _fixture.ReconcileAsync(spec.Id);

        TicketRun ticket = _fixture.Ticket(spec[7]);
        Assert.Equal(TicketRunStatus.Implementing, ticket.Status);
        Assert.Equal(1, ticket.Attempt);
        Assert.Equal($"/work/runs/{spec.Id}/tickets/{ticket.Id}", ticket.WorktreePath);
        Assert.Equal($"/work/runs/{spec.Id}/tickets/{ticket.Id}", TicketWorktreeLayout.PathFor("/work", spec.Id, ticket.Id));
    }

    [Fact]
    public async Task simultaneous_dispatcher_calls_claim_a_ready_ticket_once()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.MoveAsync(spec[1], TicketRunStatus.Ready);
        _fixture.Db.HoldSavesUntil(parties: 2);

        // Separate scopes and separate gates (e.g. a recovery path racing an event): only compare-and-swap protects the claim.
        Task<FrontierResult> first = _fixture.Frontier(_fixture.Db.OpenScope(), new ImplementerCapacityGate()).ReconcileAsync(spec.Id, TicketExecutionFixture.Token);
        Task<FrontierResult> second = _fixture.Frontier(_fixture.Db.OpenScope(), new ImplementerCapacityGate()).ReconcileAsync(spec.Id, TicketExecutionFixture.Token);
        FrontierResult[] results = await Task.WhenAll(first, second);

        Assert.Equal([spec[1]], results.SelectMany(result => result.Dispatched));
        Assert.Equal(1, _fixture.Db.ConflictCount);
        Assert.Single(_fixture.Launcher.Launched);
        Assert.Equal(1, _fixture.Ticket(spec[1]).Attempt);
        Assert.Single(_fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>(), changed => changed.To == TicketRunStatus.Implementing);
    }

    [Fact]
    public async Task per_repo_limit_caps_implementers_and_the_next_ticket_starts_when_a_slot_frees()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, []), (3, []));
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxConcurrentImplementersPerRepo = 2 });

        FrontierResult first = await _fixture.ReconcileAsync(spec.Id);
        await _fixture.MoveAsync(spec[1], TicketRunStatus.Reviewing);
        await _fixture.PumpEventsAsync();

        Assert.Equal([spec[1], spec[2]], first.Dispatched);
        Assert.Equal([spec[1], spec[2], spec[3]], _fixture.Launcher.Launched.Select(assignment => assignment.TicketRunId));
    }

    [Fact]
    public async Task global_limit_caps_implementers_across_repositories_and_a_freed_slot_goes_to_the_waiting_repository()
    {
        _fixture.Settings.Defaults = _fixture.Settings.Defaults with { MaxConcurrentImplementersGlobal = 1 };
        SeededSpec alpha = await _fixture.SeedRunningSpecAsync("alpha", (1, []));
        SeededSpec beta = await _fixture.SeedRunningSpecAsync("beta", (1, []));

        await _fixture.ReconcileAsync(alpha.Id);
        FrontierResult betaWhileFull = await _fixture.ReconcileAsync(beta.Id);
        await _fixture.MoveAsync(alpha[1], TicketRunStatus.Reviewing);
        await _fixture.PumpEventsAsync();

        Assert.Empty(betaWhileFull.Dispatched);
        Assert.Equal([beta[1]], betaWhileFull.Unblocked);
        Assert.Equal([alpha[1], beta[1]], _fixture.Launcher.Launched.Select(assignment => assignment.TicketRunId));
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(beta[1]).Status);
    }

    [Fact]
    public async Task a_fix_turn_occupies_an_implementer_slot()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]), (3, []));
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxConcurrentImplementersPerRepo = 1 });
        await _fixture.MoveAsync(spec[1], TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.FixingReviewFindings);

        FrontierResult whileFixing = await _fixture.ReconcileAsync(spec.Id);
        await _fixture.MoveAsync(spec[1], TicketRunStatus.Reviewing);
        await _fixture.PumpEventsAsync();

        Assert.Empty(whileFixing.Dispatched);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[3]).Status);
    }

    [Fact]
    public async Task capacity_counts_occupied_slots_globally_and_per_repository()
    {
        SeededSpec alpha = await _fixture.SeedRunningSpecAsync("alpha", (1, []), (2, []));
        SeededSpec beta = await _fixture.SeedRunningSpecAsync("beta", (1, []));
        await _fixture.MoveAsync(alpha[1], TicketRunStatus.Ready, TicketRunStatus.Implementing);
        await _fixture.MoveAsync(beta[1], TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.FixingReviewFindings);
        CasWorkflowScope scope = _fixture.Db.OpenScope();
        var capacity = new ImplementerCapacity(scope, scope);

        int available = await capacity.GetAvailableAsync(
            alpha.RepositoryId,
            _fixture.Settings.Defaults with { MaxConcurrentImplementersGlobal = 5, MaxConcurrentImplementersPerRepo = 3 },
            TicketExecutionFixture.Token);
        int globallyCapped = await capacity.GetAvailableAsync(
            alpha.RepositoryId,
            _fixture.Settings.Defaults with { MaxConcurrentImplementersGlobal = 2, MaxConcurrentImplementersPerRepo = 3 },
            TicketExecutionFixture.Token);

        Assert.Equal(2, available);
        Assert.Equal(0, globallyCapped);
        Assert.True(ImplementerCapacity.Occupies(TicketRunStatus.FixingReviewFindings));
        Assert.False(ImplementerCapacity.Occupies(TicketRunStatus.Reviewing));
    }
}
