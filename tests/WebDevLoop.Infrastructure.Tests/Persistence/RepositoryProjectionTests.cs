using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

/// <summary>Read projections the schedulers and startup recovery depend on.</summary>
public sealed class RepositoryProjectionTests : IDisposable
{
    private readonly PersistenceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task non_terminal_spec_runs_include_parked_runs_but_not_completed_or_aborted_ones()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope scope = _harness.OpenScope())
        {
            SpecRun parked = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
            SpecRun aborted = TestData.NewSpecRun(repositoryId, "run-3", issueNumber: 30, queuePosition: 3);
            SpecRun completed = TestData.NewSpecRun(repositoryId, "run-4", issueNumber: 40, queuePosition: 4);
            scope.SpecRuns.Add(parked);
            scope.SpecRuns.Add(aborted);
            scope.SpecRuns.Add(completed);
            parked.MarkNeedsAttention("limit", TestData.Now);
            aborted.TransitionTo(SpecRunStatus.Aborted, TestData.Now);
            completed.TransitionTo(SpecRunStatus.Preparing, TestData.Now);
            completed.TransitionTo(SpecRunStatus.Running, TestData.Now);
            completed.TransitionTo(SpecRunStatus.ParentReviewing, TestData.Now);
            completed.TransitionTo(SpecRunStatus.Testing, TestData.Now);
            completed.TransitionTo(SpecRunStatus.Completed, TestData.Now);
            await scope.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        IReadOnlyList<SpecRun> nonTerminal = await read.SpecRuns.ListNonTerminalAsync(CancellationToken.None);

        Assert.Equal(["run-1", "run-2"], nonTerminal.Select(run => run.Id.Value));
        Assert.Equal(4, (await read.SpecRuns.ListByRepositoryAsync(repositoryId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task spec_runs_of_a_repository_are_ordered_by_queue_position()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope scope = _harness.OpenScope())
        {
            scope.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-z", issueNumber: 20, queuePosition: 0));
            scope.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-a", issueNumber: 30, queuePosition: 2));
            await scope.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();

        Assert.Equal(
            ["run-z", "run-1", "run-a"],
            (await read.SpecRuns.ListByRepositoryAsync(repositoryId, CancellationToken.None)).Select(run => run.Id.Value));
    }

    [Fact]
    public async Task active_steps_are_pending_or_running_only()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope scope = _harness.OpenScope())
        {
            StepRun pending = TestData.NewStep(specRunId, ticketId, "s-pending", StepKind.Review, AgentRole.ReviewerCodingStandards);
            StepRun running = TestData.NewStep(specRunId, ticketId, "s-running", StepKind.Review, AgentRole.ReviewerSpecification);
            StepRun failed = TestData.NewStep(specRunId, ticketId, "s-failed", StepKind.Implement, AgentRole.Implementer);
            running.Start(TestData.Now, TimeSpan.FromMinutes(1));
            failed.Start(TestData.Now, TimeSpan.FromMinutes(1));
            failed.Finish(StepStatus.Failed, TestData.Now, failureReason: "boom");
            scope.Steps.Add(pending);
            scope.Steps.Add(running);
            scope.Steps.Add(failed);
            await scope.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();

        Assert.Equal(["s-pending", "s-running"], (await read.Steps.ListActiveAsync(CancellationToken.None)).Select(step => step.Id.Value));
        Assert.Equal(3, (await read.Steps.ListBySpecRunAsync(specRunId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task incomplete_sagas_and_active_leases_exclude_finished_ones()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope scope = _harness.OpenScope())
        {
            IntegrationSaga done = IntegrationSaga.Start(specRunId, ticketId, null, TestData.Now);
            done.AdvanceTo(IntegrationSagaCheckpoint.Completed, TestData.Now);
            scope.Sagas.Add(done);
            TestLease released = TestLease.Acquire(specRunId, 5100, "/work/old", TestData.Now, TimeSpan.FromMinutes(5));
            released.Release(TestData.Now.AddMinutes(1));
            scope.Leases.Add(released);
            await scope.SaveAsync();

            scope.Sagas.Add(IntegrationSaga.Start(specRunId, ticketId, TestData.Sha1, TestData.Now));
            scope.Leases.Add(TestLease.Acquire(specRunId, 5100, "/work/new", TestData.Now, TimeSpan.FromMinutes(5)));
            await scope.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        IntegrationSaga incomplete = Assert.Single(await read.Sagas.ListIncompleteAsync(CancellationToken.None));
        TestLease active = Assert.Single(await read.Leases.ListActiveAsync(CancellationToken.None));

        Assert.Equal(IntegrationSagaCheckpoint.Started, incomplete.Checkpoint);
        Assert.Equal(TestData.Sha1, incomplete.ExpectedPriorIntegrationSha);
        Assert.Equal("/work/new", active.WorkspacePath);
        Assert.Equal(incomplete.Id, (await read.Sagas.FindLatestForTicketAsync(ticketId, CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task pending_outbox_rows_are_returned_oldest_first_up_to_the_limit_and_dispatched_ones_are_skipped()
    {
        using (PersistenceScope scope = _harness.OpenScope())
        {
            for (int index = 1; index <= 4; index++)
            {
                scope.Outbox.Add(OutboxMessage.Create($"Event{index}", "{}", TestData.Now.AddSeconds(index)));
            }

            await scope.SaveAsync();
        }

        using (PersistenceScope dispatch = _harness.OpenScope())
        {
            OutboxMessage first = (await dispatch.Outbox.ListPendingAsync(1, CancellationToken.None)).Single();
            first.MarkDispatched(TestData.Now.AddMinutes(1));
            await dispatch.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        IReadOnlyList<OutboxMessage> pending = await read.Outbox.ListPendingAsync(2, CancellationToken.None);

        Assert.Equal(["Event2", "Event3"], pending.Select(message => message.Type));
        Assert.Equal("Event4", (await read.Outbox.ListPendingAsync(10, CancellationToken.None))[2].Type);
        Assert.Equal(TestData.Now.AddMinutes(1), (await read.Outbox.GetAsync(1, CancellationToken.None))!.DispatchedAt);
    }

    [Fact]
    public async Task removing_a_repository_cascades_to_its_settings_override_but_is_blocked_by_its_runs()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope scope = _harness.OpenScope())
        {
            scope.Settings.Add(SettingsProfile.ForRepository(repositoryId));
            await scope.SaveAsync();
            scope.Repositories.Remove((await scope.Repositories.GetAsync(repositoryId, CancellationToken.None))!);

            await Assert.ThrowsAnyAsync<Exception>(() => scope.UnitOfWork.SaveChangesAsync(CancellationToken.None));
        }

        using PersistenceScope emptyRepository = _harness.OpenScope();
        RepositoryRecord other = TestData.NewRepository("acme", "gadgets");
        emptyRepository.Repositories.Add(other);
        await emptyRepository.SaveAsync();
        emptyRepository.Settings.Add(SettingsProfile.ForRepository(other.Id));
        await emptyRepository.SaveAsync();
        emptyRepository.Repositories.Remove(other);
        await emptyRepository.SaveAsync();

        using PersistenceScope read = _harness.OpenScope();
        Assert.Null(await read.Settings.FindForRepositoryAsync(other.Id, CancellationToken.None));
        Assert.Single(await read.Repositories.ListAsync(CancellationToken.None));
    }
}
