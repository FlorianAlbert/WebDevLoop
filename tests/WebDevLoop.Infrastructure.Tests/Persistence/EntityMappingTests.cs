using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

public sealed class EntityMappingTests : IDisposable
{
    private readonly PersistenceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task repository_record_round_trips_and_is_found_by_ref_ignoring_case()
    {
        using (PersistenceScope write = _harness.OpenScope())
        {
            RepositoryRecord repository = TestData.NewRepository();
            repository.SetEnabled(true, TestData.Now.AddMinutes(1));
            write.Repositories.Add(repository);
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        RepositoryRecord? loaded = await read.Repositories.FindAsync(new GitHubRepoRef("ACME", "Widgets"), CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(loaded.Id > 0);
        Assert.Equal(new GitHubRepoRef("acme", "widgets"), loaded.Ref);
        Assert.Equal(new BranchName("main"), loaded.DefaultBaseBranch);
        Assert.Equal("https://github.com/acme/widgets.git", loaded.CloneUrl);
        Assert.Equal("/work/acme/widgets", loaded.LocalPath);
        Assert.True(loaded.IsEnabled);
        Assert.Equal(TestData.Now, loaded.CreatedAt);
        Assert.Equal(TestData.Now.AddMinutes(1), loaded.UpdatedAt);
    }

    [Fact]
    public async Task spec_run_round_trips_all_state_and_dependencies()
    {
        (int repositoryId, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            SpecRun other = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
            write.SpecRuns.Add(other);
            SpecRun run = (await write.SpecRuns.GetAsync(specRunId, CancellationToken.None))!;
            run.TransitionTo(SpecRunStatus.Preparing, TestData.Now);
            run.BaseBranch = new BranchName("main");
            run.IntegrationBaseSha = TestData.Sha1;
            run.IntegrationTipSha = TestData.Sha2;
            run.DependencyModeUsed = SpecDependencyMode.StackOnTop;
            run.MaxActiveSpecsSlot = 1;
            run.TransitionTo(SpecRunStatus.Running, TestData.Now);
            write.SpecRuns.AddDependency(SpecDependency.OnSpecRun(other.Id, run.Id, SpecDependencyMode.StackOnTop));
            write.SpecRuns.AddDependency(SpecDependency.OnExternalIssue(other.Id, new IssueRef("acme", "widgets", 5, "I_5", 55), SpecDependencyMode.WaitForMerge));
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        SpecRun loaded = (await read.SpecRuns.GetAsync(specRunId, CancellationToken.None))!;

        Assert.Equal(repositoryId, loaded.RepositoryId);
        Assert.Equal(new IssueRef("acme", "widgets", 10, "I_node", 99), loaded.ParentIssue);
        Assert.Equal("Spec title", loaded.Title);
        Assert.Equal("Spec body", loaded.BodySnapshot);
        Assert.Equal(SpecRunStatus.Running, loaded.Status);
        Assert.Equal(1, loaded.QueuePosition);
        Assert.Equal(new BranchName("main"), loaded.BaseBranch);
        Assert.Equal(RunScopedNaming.IntegrationBranch(specRunId), loaded.IntegrationBranch);
        Assert.Equal(TestData.Sha1, loaded.IntegrationBaseSha);
        Assert.Equal(TestData.Sha2, loaded.IntegrationTipSha);
        Assert.Equal(SpecDependencyMode.StackOnTop, loaded.DependencyModeUsed);
        Assert.Equal(1, loaded.MaxActiveSpecsSlot);
        Assert.Equal(TestData.Now, loaded.StartedAt);
        Assert.Null(loaded.CompletedAt);

        IReadOnlyList<SpecDependency> dependencies = await read.SpecRuns.ListDependenciesAsync(new RunId("run-2"), CancellationToken.None);
        Assert.Equal(2, dependencies.Count);
        Assert.Equal(specRunId, dependencies[0].BlockingSpecRunId);
        Assert.Null(dependencies[0].ExternalBlockingIssue);
        Assert.Null(dependencies[1].BlockingSpecRunId);
        Assert.Equal(new IssueRef("acme", "widgets", 5, "I_5", 55), dependencies[1].ExternalBlockingIssue);
        Assert.Equal(SpecDependencyMode.WaitForMerge, dependencies[1].ModeAtStart);
    }

    [Fact]
    public async Task ticket_run_round_trips_all_state_and_dependencies()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            TicketRun blocker = TestData.NewTicketRun(specRunId, "t-2", 12);
            write.Tickets.Add(blocker);
            TicketRun ticket = (await write.Tickets.GetAsync(ticketId, CancellationToken.None))!;
            ticket.TransitionTo(TicketRunStatus.Ready, TestData.Now);
            ticket.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
            ticket.WorktreePath = "/work/t-1";
            ticket.LastImplementedSha = TestData.Sha1;
            ticket.IntegratedCommitSha = TestData.Sha2;
            ticket.PullRequestNumber = new PullRequestNumber(7);
            ticket.StackPosition = 2;
            write.Tickets.AddDependency(TicketDependency.Create(specRunId, ticketId, blocker.Id, DependencySource.CreatedFinding));
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        TicketRun loaded = (await read.Tickets.GetAsync(ticketId, CancellationToken.None))!;

        Assert.Equal(specRunId, loaded.SpecRunId);
        Assert.Equal(new IssueRef("acme", "widgets", 11), loaded.Issue);
        Assert.Equal(TicketRunStatus.Implementing, loaded.Status);
        Assert.Equal(1, loaded.Attempt);
        Assert.Equal(RunScopedNaming.TicketBranch(specRunId, ticketId), loaded.BranchName);
        Assert.Equal("/work/t-1", loaded.WorktreePath);
        Assert.Equal(TestData.Sha1, loaded.LastImplementedSha);
        Assert.Equal(TestData.Sha2, loaded.IntegratedCommitSha);
        Assert.Equal(new PullRequestNumber(7), loaded.PullRequestNumber);
        Assert.Equal(2, loaded.StackPosition);

        TicketDependency dependency = Assert.Single(await read.Tickets.ListDependenciesAsync(specRunId, CancellationToken.None));
        Assert.Equal(ticketId, dependency.BlockedTicketRunId);
        Assert.Equal(new TicketRunId("t-2"), dependency.BlockingTicketRunId);
        Assert.Equal(DependencySource.CreatedFinding, dependency.Source);
        Assert.Equal(2, (await read.Tickets.ListBySpecRunAsync(specRunId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task step_run_round_trips_all_state()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            StepRun step = TestData.NewStep(specRunId, ticketId, "s-1", StepKind.Implement, AgentRole.Implementer);
            step.Start(TestData.Now, TimeSpan.FromMinutes(30));
            step.CopilotSessionId = "session-1";
            step.WorktreePath = "/work/t-1";
            step.BranchName = new BranchName("webdevloop/run-1/ticket/t-1");
            step.Finish(StepStatus.Succeeded, TestData.Now.AddMinutes(5), "{\"ok\":true}");
            write.Steps.Add(step);
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        StepRun loaded = (await read.Steps.GetAsync(new StepRunId("s-1"), CancellationToken.None))!;

        Assert.Equal(specRunId, loaded.SpecRunId);
        Assert.Equal(ticketId, loaded.TicketRunId);
        Assert.Equal(StepKind.Implement, loaded.Kind);
        Assert.Equal(AgentRole.Implementer, loaded.AgentRole);
        Assert.Equal(StepStatus.Succeeded, loaded.Status);
        Assert.Equal(1, loaded.Attempt);
        Assert.Equal("session-1", loaded.CopilotSessionId);
        Assert.Equal("/work/t-1", loaded.WorktreePath);
        Assert.Equal(new BranchName("webdevloop/run-1/ticket/t-1"), loaded.BranchName);
        Assert.Equal(TestData.Now, loaded.StartedAt);
        Assert.Equal(TestData.Now.AddMinutes(30), loaded.TimeoutAt);
        Assert.Equal(TestData.Now.AddMinutes(5), loaded.CompletedAt);
        Assert.Equal("prompt-hash", loaded.InputPromptHash);
        Assert.Equal("{\"ok\":true}", loaded.StructuredResultJson);
        Assert.Single(await read.Steps.ListByTicketRunAsync(ticketId, CancellationToken.None));
        Assert.Empty(await read.Steps.ListActiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task integration_saga_and_pull_stack_layer_round_trip_with_checkpoint()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            IntegrationSaga saga = IntegrationSaga.Start(specRunId, ticketId, TestData.Sha1, TestData.Now);
            saga.SquashCommitSha = TestData.Sha2;
            saga.PullRequestNumber = new PullRequestNumber(9);
            saga.StackNumber = 3;
            saga.AdvanceTo(IntegrationSagaCheckpoint.PrCreated, TestData.Now.AddMinutes(1));
            saga.RecordError("push rejected", TestData.Now.AddMinutes(2));
            write.Sagas.Add(saga);

            PullStackLayer layer = PullStackLayer.Create(
                specRunId, ticketId, new BranchName("stack/run-1/t-1"), TestData.Sha2, new PullRequestNumber(9), new BranchName("main"), 1, TestData.Now);
            layer.StackNumber = 3;
            layer.RecordVerifiedDiff(TestData.Sha1, TestData.Now.AddMinutes(3));
            write.Layers.Add(layer);
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        IntegrationSaga saga2 = (await read.Sagas.FindLatestForTicketAsync(ticketId, CancellationToken.None))!;
        Assert.Equal(specRunId, saga2.SpecRunId);
        Assert.Equal(TestData.Sha1, saga2.ExpectedPriorIntegrationSha);
        Assert.Equal(TestData.Sha2, saga2.SquashCommitSha);
        Assert.Equal(RunScopedNaming.StackBranch(specRunId, ticketId), saga2.StackBranchName);
        Assert.Equal(new PullRequestNumber(9), saga2.PullRequestNumber);
        Assert.Equal(3, saga2.StackNumber);
        Assert.Equal(IntegrationSagaCheckpoint.PrCreated, saga2.Checkpoint);
        Assert.Equal($"{specRunId}:{ticketId}", saga2.ExternalIdempotencyKey);
        Assert.Equal("push rejected", saga2.LastError);
        Assert.Single(await read.Sagas.ListIncompleteAsync(CancellationToken.None));

        PullStackLayer layer2 = Assert.Single(await read.Layers.ListBySpecRunAsync(specRunId, CancellationToken.None));
        Assert.Equal(new BranchName("stack/run-1/t-1"), layer2.BranchName);
        Assert.Equal(TestData.Sha2, layer2.CommitSha);
        Assert.Equal(new PullRequestNumber(9), layer2.PullRequestNumber);
        Assert.Equal(new BranchName("main"), layer2.BaseBranch);
        Assert.Equal(3, layer2.StackNumber);
        Assert.Equal(1, layer2.Position);
        Assert.True(layer2.IsDraft);
        Assert.Equal(TestData.Sha1, layer2.VerifiedDiffSha);
    }

    [Fact]
    public async Task finding_issuance_test_lease_run_event_and_outbox_message_round_trip()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            StepRun step = TestData.NewStep(specRunId, null, "s-parent", StepKind.ParentReview, AgentRole.ReviewerSpecification);
            write.Steps.Add(step);
            await write.SaveAsync();

            FindingIssuance finding = FindingIssuance.Plan(specRunId, step.Id, FindingAxis.Specification, new FindingFingerprint("  Missing   Validation "), TestData.Now);
            finding.RecordCreated(42, 4242, TestData.Now.AddMinutes(1));
            write.Findings.Add(finding);

            TestLease lease = TestLease.Acquire(specRunId, 5100, "/work/test", TestData.Now, TimeSpan.FromMinutes(20));
            lease.AttachProcess(1234);
            write.Leases.Add(lease);

            write.Events.Add(RunEvent.Create(specRunId, ticketId, "TicketRunStatusChanged", "{}", TestData.Now));
            write.Outbox.Add(OutboxMessage.Create("SpecRunStatusChanged", "{\"x\":1}", TestData.Now));
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        FindingIssuance loadedFinding = (await read.Findings.FindAsync(specRunId, new FindingFingerprint("missing validation"), CancellationToken.None))!;
        Assert.Equal(new StepRunId("s-parent"), loadedFinding.SourceStepRunId);
        Assert.Equal(FindingAxis.Specification, loadedFinding.Axis);
        Assert.Equal(42, loadedFinding.IssueNumber);
        Assert.Equal(4242, loadedFinding.IssueDatabaseId);
        Assert.Equal(FindingIssuanceStatus.Created, loadedFinding.Status);

        TestLease loadedLease = (await read.Leases.FindActiveAsync(specRunId, CancellationToken.None))!;
        Assert.Equal(5100, loadedLease.Port);
        Assert.Equal("/work/test", loadedLease.WorkspacePath);
        Assert.Equal(1234, loadedLease.ProcessId);
        Assert.Equal(TestData.Now.AddMinutes(20), loadedLease.ExpiresAt);
        Assert.Null(loadedLease.ReleasedAt);

        RunEvent runEvent = Assert.Single(await read.Events.ListBySpecRunAsync(specRunId, CancellationToken.None));
        Assert.Equal(ticketId, runEvent.TicketRunId);
        Assert.Equal("TicketRunStatusChanged", runEvent.Type);

        OutboxMessage message = Assert.Single(await read.Outbox.ListPendingAsync(10, CancellationToken.None));
        Assert.Equal("SpecRunStatusChanged", message.Type);
        Assert.Equal("{\"x\":1}", message.PayloadJson);
        Assert.True(message.IsPending);
    }
}
