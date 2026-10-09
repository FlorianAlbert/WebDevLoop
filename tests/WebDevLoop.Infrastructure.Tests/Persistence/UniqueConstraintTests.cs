using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

/// <summary>Database-enforced uniqueness. A write that loses a race on a unique index is reported as a conflict, like a lost version race.</summary>
public sealed class UniqueConstraintTests : IDisposable
{
    private static readonly RunId FirstRun = new("run-1");

    private readonly PersistenceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task second_active_spec_in_the_same_repository_conflicts_under_the_default_single_slot()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        await ActivateAsync(FirstRun, slot: 1);

        using PersistenceScope scope = _harness.OpenScope();
        SpecRun second = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
        scope.SpecRuns.Add(second);
        Activate(second, slot: 1);

        Assert.Equal(SaveOutcome.ConcurrencyConflict, await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None));
        using PersistenceScope read = _harness.OpenScope();
        Assert.Single(await read.SpecRuns.ListByRepositoryAsync(repositoryId, CancellationToken.None));
    }

    [Fact]
    public async Task concurrent_activation_of_two_specs_in_one_repository_lets_exactly_one_win()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope seed = _harness.OpenScope())
        {
            seed.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2));
            await seed.SaveAsync();
        }

        using PersistenceScope first = _harness.OpenScope();
        using PersistenceScope second = _harness.OpenScope();
        Activate((await first.SpecRuns.GetAsync(FirstRun, CancellationToken.None))!, slot: 1);
        Activate((await second.SpecRuns.GetAsync(new RunId("run-2"), CancellationToken.None))!, slot: 1);

        SaveOutcome[] outcomes = await Task.WhenAll(
            Task.Run(() => first.UnitOfWork.SaveChangesAsync(CancellationToken.None)),
            Task.Run(() => second.UnitOfWork.SaveChangesAsync(CancellationToken.None)));

        Assert.Single(outcomes, SaveOutcome.Saved);
        Assert.Single(outcomes, SaveOutcome.ConcurrencyConflict);
    }

    [Fact]
    public async Task specs_in_different_slots_other_repositories_or_inactive_states_do_not_conflict()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        await ActivateAsync(FirstRun, slot: 1);

        using PersistenceScope scope = _harness.OpenScope();
        RepositoryRecord otherRepository = TestData.NewRepository("acme", "gadgets");
        scope.Repositories.Add(otherRepository);
        await scope.SaveAsync();

        SpecRun secondSlot = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
        SpecRun queued = TestData.NewSpecRun(repositoryId, "run-3", issueNumber: 30, queuePosition: 3);
        SpecRun otherRepo = TestData.NewSpecRun(otherRepository.Id, "run-4", issueNumber: 40, queuePosition: 1);
        scope.SpecRuns.Add(secondSlot);
        scope.SpecRuns.Add(queued);
        scope.SpecRuns.Add(otherRepo);
        Activate(secondSlot, slot: 2);
        Activate(otherRepo, slot: 1);
        queued.MaxActiveSpecsSlot = 1;

        await scope.SaveAsync();
    }

    [Fact]
    public async Task a_slot_can_be_reused_once_its_spec_leaves_the_active_states()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        await ActivateAsync(FirstRun, slot: 1);
        using (PersistenceScope finish = _harness.OpenScope())
        {
            SpecRun first = (await finish.SpecRuns.GetAsync(FirstRun, CancellationToken.None))!;
            first.MarkNeedsAttention("cycle limit", TestData.Now);
            await finish.SaveAsync();
        }

        using PersistenceScope scope = _harness.OpenScope();
        SpecRun second = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
        scope.SpecRuns.Add(second);
        Activate(second, slot: 1);

        await scope.SaveAsync();
    }

    [Fact]
    public async Task a_new_spec_can_be_active_while_another_is_ready_for_review_or_awaiting_merge()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        await ActivateAsync(FirstRun, slot: 1);
        using (PersistenceScope review = _harness.OpenScope())
        {
            SpecRun first = (await review.SpecRuns.GetAsync(FirstRun, CancellationToken.None))!;
            foreach (SpecRunStatus next in new[]
            {
                SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, SpecRunStatus.ReadyForReview,
            })
            {
                first.TransitionTo(next, TestData.Now);
            }

            // A leftover slot value must not matter: the index only covers specs that occupy a slot.
            first.MaxActiveSpecsSlot = 1;
            await review.SaveAsync();
        }

        using PersistenceScope scope = _harness.OpenScope();
        SpecRun second = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
        scope.SpecRuns.Add(second);
        Activate(second, slot: 1);

        await scope.SaveAsync();

        using PersistenceScope read = _harness.OpenScope();
        Assert.Equal(2, (await read.SpecRuns.ListByRepositoryAsync(repositoryId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task second_active_implement_or_fix_step_for_a_ticket_conflicts_but_review_steps_may_overlap()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope first = _harness.OpenScope())
        {
            first.Steps.Add(TestData.NewStep(specRunId, ticketId, "s-impl", StepKind.Implement, AgentRole.Implementer));
            first.Steps.Add(TestData.NewStep(specRunId, ticketId, "s-review-1", StepKind.Review, AgentRole.ReviewerCodingStandards));
            first.Steps.Add(TestData.NewStep(specRunId, ticketId, "s-review-2", StepKind.Review, AgentRole.ReviewerSpecification));
            await first.SaveAsync();
        }

        using PersistenceScope scope = _harness.OpenScope();
        scope.Steps.Add(TestData.NewStep(specRunId, ticketId, "s-fix", StepKind.Fix, AgentRole.Implementer));

        Assert.Equal(SaveOutcome.ConcurrencyConflict, await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task a_new_implement_or_fix_step_is_allowed_after_the_previous_one_finished()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope first = _harness.OpenScope())
        {
            StepRun implement = TestData.NewStep(specRunId, ticketId, "s-impl", StepKind.Implement, AgentRole.Implementer);
            implement.Start(TestData.Now, TimeSpan.FromMinutes(5));
            implement.Finish(StepStatus.Succeeded, TestData.Now);
            first.Steps.Add(implement);
            await first.SaveAsync();
        }

        using PersistenceScope scope = _harness.OpenScope();
        scope.Steps.Add(TestData.NewStep(specRunId, ticketId, "s-fix", StepKind.Fix, AgentRole.Implementer));

        await scope.SaveAsync();
    }

    [Fact]
    public async Task second_active_app_owned_step_of_the_same_kind_in_a_run_conflicts()
    {
        (int _, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope first = _harness.OpenScope())
        {
            first.Steps.Add(TestData.NewStep(specRunId, null, "s-app-1", StepKind.Explore));
            await first.SaveAsync();
        }

        using PersistenceScope scope = _harness.OpenScope();
        scope.Steps.Add(TestData.NewStep(specRunId, null, "s-app-2", StepKind.Explore));

        Assert.Equal(SaveOutcome.ConcurrencyConflict, await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task finding_fingerprint_is_unique_per_spec_run_after_normalisation()
    {
        (int repositoryId, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);
        await AddStepAsync(specRunId, "s-review-1");
        await AddStepAsync(specRunId, "s-review-2");
        using (PersistenceScope first = _harness.OpenScope())
        {
            first.Findings.Add(FindingIssuance.Plan(specRunId, new StepRunId("s-review-1"), FindingAxis.Specification, new FindingFingerprint("Missing validation"), TestData.Now));
            await first.SaveAsync();
        }

        using PersistenceScope duplicate = _harness.OpenScope();
        duplicate.Findings.Add(FindingIssuance.Plan(specRunId, new StepRunId("s-review-2"), FindingAxis.Specification, new FindingFingerprint("  missing   VALIDATION "), TestData.Now));
        Assert.Equal(SaveOutcome.ConcurrencyConflict, await duplicate.UnitOfWork.SaveChangesAsync(CancellationToken.None));

        using PersistenceScope otherRun = _harness.OpenScope();
        SpecRun second = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
        otherRun.SpecRuns.Add(second);
        otherRun.Steps.Add(TestData.NewStep(second.Id, null, "s-other", StepKind.ParentReview, AgentRole.ReviewerSpecification));
        await otherRun.SaveAsync();
        otherRun.Findings.Add(FindingIssuance.Plan(second.Id, new StepRunId("s-other"), FindingAxis.Specification, new FindingFingerprint("Missing validation"), TestData.Now));
        await otherRun.SaveAsync();
    }

    [Fact]
    public async Task only_one_active_test_lease_per_run_and_per_port_until_released()
    {
        (int _, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope first = _harness.OpenScope())
        {
            first.Leases.Add(TestLease.Acquire(specRunId, 5100, "/work/a", TestData.Now, TimeSpan.FromMinutes(10)));
            await first.SaveAsync();
        }

        using (PersistenceScope secondLease = _harness.OpenScope())
        {
            secondLease.Leases.Add(TestLease.Acquire(specRunId, 5101, "/work/b", TestData.Now, TimeSpan.FromMinutes(10)));
            Assert.Equal(SaveOutcome.ConcurrencyConflict, await secondLease.UnitOfWork.SaveChangesAsync(CancellationToken.None));
        }

        using (PersistenceScope release = _harness.OpenScope())
        {
            TestLease active = (await release.Leases.FindActiveAsync(specRunId, CancellationToken.None))!;
            active.Release(TestData.Now.AddMinutes(1));
            release.Leases.Add(TestLease.Acquire(specRunId, 5100, "/work/c", TestData.Now.AddMinutes(2), TimeSpan.FromMinutes(10)));
            await release.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        Assert.Equal("/work/c", (await read.Leases.FindActiveAsync(specRunId, CancellationToken.None))!.WorkspacePath);
    }

    [Fact]
    public async Task one_port_cannot_be_leased_to_two_runs_at_once()
    {
        (int repositoryId, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope first = _harness.OpenScope())
        {
            first.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2));
            first.Leases.Add(TestLease.Acquire(specRunId, 5100, "/work/a", TestData.Now, TimeSpan.FromMinutes(10)));
            await first.SaveAsync();
        }

        using PersistenceScope scope = _harness.OpenScope();
        scope.Leases.Add(TestLease.Acquire(new RunId("run-2"), 5100, "/work/b", TestData.Now, TimeSpan.FromMinutes(10)));

        Assert.Equal(SaveOutcome.ConcurrencyConflict, await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task only_one_incomplete_integration_saga_per_ticket()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope first = _harness.OpenScope())
        {
            first.Sagas.Add(IntegrationSaga.Start(specRunId, ticketId, null, TestData.Now));
            await first.SaveAsync();
        }

        using (PersistenceScope duplicate = _harness.OpenScope())
        {
            duplicate.Sagas.Add(IntegrationSaga.Start(specRunId, ticketId, null, TestData.Now));
            Assert.Equal(SaveOutcome.ConcurrencyConflict, await duplicate.UnitOfWork.SaveChangesAsync(CancellationToken.None));
        }

        using PersistenceScope complete = _harness.OpenScope();
        IntegrationSaga saga = (await complete.Sagas.FindLatestForTicketAsync(ticketId, CancellationToken.None))!;
        saga.AdvanceTo(IntegrationSagaCheckpoint.Completed, TestData.Now);
        complete.Sagas.Add(IntegrationSaga.Start(specRunId, ticketId, TestData.Sha1, TestData.Now));
        await complete.SaveAsync();
    }

    [Fact]
    public async Task repository_identity_settings_scope_dependencies_and_stack_positions_are_unique()
    {
        (int repositoryId, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope seed = _harness.OpenScope())
        {
            seed.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2));
            seed.Tickets.Add(TestData.NewTicketRun(specRunId, "t-1"));
            seed.Tickets.Add(TestData.NewTicketRun(specRunId, "t-2", 12));
            seed.Settings.Add(SettingsProfile.ForGlobal());
            seed.Settings.Add(SettingsProfile.ForRepository(repositoryId));
            seed.SpecRuns.AddDependency(SpecDependency.OnSpecRun(new RunId("run-2"), specRunId, SpecDependencyMode.WaitForMerge));
            seed.Tickets.AddDependency(TicketDependency.Create(specRunId, new TicketRunId("t-2"), new TicketRunId("t-1"), DependencySource.GitHub));
            seed.Layers.Add(NewLayer(specRunId, "t-1", position: 1));
            await seed.SaveAsync();
        }

        await AssertConflictAsync(scope => scope.Repositories.Add(TestData.NewRepository("ACME", "Widgets")));
        await AssertConflictAsync(scope => scope.Settings.Add(SettingsProfile.ForGlobal()));
        await AssertConflictAsync(scope => scope.Settings.Add(SettingsProfile.ForRepository(repositoryId)));
        await AssertConflictAsync(scope => scope.SpecRuns.AddDependency(SpecDependency.OnSpecRun(new RunId("run-2"), specRunId, SpecDependencyMode.StackOnTop)));
        await AssertConflictAsync(scope => scope.Tickets.AddDependency(TicketDependency.Create(specRunId, new TicketRunId("t-2"), new TicketRunId("t-1"), DependencySource.CreatedFinding)));
        await AssertConflictAsync(scope => scope.Layers.Add(NewLayer(specRunId, "t-2", position: 1)));
    }

    private static PullStackLayer NewLayer(RunId specRunId, string ticketId, int position) =>
        PullStackLayer.Create(
            specRunId,
            new TicketRunId(ticketId),
            new BranchName($"stack/run-1/{ticketId}"),
            TestData.Sha1,
            new PullRequestNumber(position),
            new BranchName("main"),
            position,
            TestData.Now);

    private async Task AssertConflictAsync(Action<PersistenceScope> arrange)
    {
        using PersistenceScope scope = _harness.OpenScope();
        arrange(scope);

        Assert.Equal(SaveOutcome.ConcurrencyConflict, await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None));
    }

    private async Task AddStepAsync(RunId specRunId, string stepId)
    {
        using PersistenceScope scope = _harness.OpenScope();
        scope.Steps.Add(TestData.NewStep(specRunId, null, stepId, StepKind.ParentReview, AgentRole.ReviewerSpecification));
        await scope.SaveAsync();
    }

    private async Task ActivateAsync(RunId specRunId, int slot)
    {
        using PersistenceScope scope = _harness.OpenScope();
        Activate((await scope.SpecRuns.GetAsync(specRunId, CancellationToken.None))!, slot);
        await scope.SaveAsync();
    }

    private static void Activate(SpecRun run, int slot)
    {
        run.TransitionTo(SpecRunStatus.Preparing, TestData.Now);
        run.MaxActiveSpecsSlot = slot;
    }
}
