using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain;

public sealed class SupportingEntityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly RunId Run = new("run1");
    private static readonly TicketRunId Ticket = new("t1");
    private static readonly CommitSha ShaA = new(new string('a', 40));
    private static readonly CommitSha ShaB = new(new string('b', 40));

    [Fact]
    public void spec_dependency_on_another_run_keeps_mode_at_start()
    {
        var dependency = SpecDependency.OnSpecRun(new RunId("run2"), Run, SpecDependencyMode.StackOnTop);

        Assert.Equal(Run, dependency.BlockingSpecRunId);
        Assert.Null(dependency.ExternalBlockingIssue);
        Assert.Equal(SpecDependencyMode.StackOnTop, dependency.ModeAtStart);
        Assert.Equal(DependencySource.GitHub, dependency.Source);
    }

    [Fact]
    public void spec_dependency_on_itself_is_rejected()
    {
        Assert.Throws<DependencyCycleException>(() => SpecDependency.OnSpecRun(Run, Run, SpecDependencyMode.WaitForMerge));
    }

    [Fact]
    public void spec_dependency_on_external_issue_has_no_blocking_run()
    {
        var issue = new IssueRef("o", "r", 3);

        var dependency = SpecDependency.OnExternalIssue(Run, issue, SpecDependencyMode.WaitForMerge);

        Assert.Null(dependency.BlockingSpecRunId);
        Assert.Equal(issue, dependency.ExternalBlockingIssue);
    }

    [Fact]
    public void ticket_dependency_rejects_self_links_and_exposes_an_edge()
    {
        Assert.Throws<DependencyCycleException>(() => TicketDependency.Create(Run, Ticket, Ticket, DependencySource.GitHub));

        var dependency = TicketDependency.Create(Run, Ticket, new TicketRunId("t2"), DependencySource.CreatedFinding);

        Assert.Equal(new DependencyEdge<TicketRunId>(Ticket, new TicketRunId("t2")), dependency.ToEdge());
    }

    [Fact]
    public void ticket_dependencies_feed_the_cycle_validator()
    {
        var existing = new[]
        {
            TicketDependency.Create(Run, new TicketRunId("t1"), new TicketRunId("t2"), DependencySource.GitHub).ToEdge(),
            TicketDependency.Create(Run, new TicketRunId("t2"), new TicketRunId("t3"), DependencySource.GitHub).ToEdge(),
        };

        Assert.Throws<DependencyCycleException>(() =>
            DependencyGraph.EnsureCanAdd(existing, new DependencyEdge<TicketRunId>(new TicketRunId("t3"), new TicketRunId("t1"))));
    }

    [Fact]
    public void saga_starts_at_started_with_stable_idempotency_key_and_stack_branch()
    {
        var saga = IntegrationSaga.Start(Run, Ticket, ShaA, T0);

        Assert.Equal(IntegrationSagaCheckpoint.Started, saga.Checkpoint);
        Assert.Equal("stack/run1/t1", saga.StackBranchName.Value);
        Assert.Equal("run1:t1", saga.ExternalIdempotencyKey);
        Assert.Equal(ShaA, saga.ExpectedPriorIntegrationSha);
    }

    [Fact]
    public void saga_only_moves_forward_through_checkpoints()
    {
        var saga = IntegrationSaga.Start(Run, Ticket, null, T0);

        saga.AdvanceTo(IntegrationSagaCheckpoint.IntegrationPushed, T0.AddMinutes(1));

        Assert.Equal(IntegrationSagaCheckpoint.IntegrationPushed, saga.Checkpoint);
        Assert.Equal(T0.AddMinutes(1), saga.UpdatedAt);
        Assert.Throws<InvalidStatusTransitionException>(() => saga.AdvanceTo(IntegrationSagaCheckpoint.SquashCommitCreated, T0));
        Assert.Equal(IntegrationSagaCheckpoint.IntegrationPushed, saga.Checkpoint);
    }

    [Fact]
    public void saga_re_advancing_to_the_same_checkpoint_is_a_no_op_for_recovery()
    {
        var saga = IntegrationSaga.Start(Run, Ticket, null, T0);
        saga.AdvanceTo(IntegrationSagaCheckpoint.PrCreated, T0);

        saga.AdvanceTo(IntegrationSagaCheckpoint.PrCreated, T0.AddMinutes(1));

        Assert.Equal(IntegrationSagaCheckpoint.PrCreated, saga.Checkpoint);
    }

    [Fact]
    public void saga_is_completed_only_at_the_final_checkpoint_and_records_errors()
    {
        var saga = IntegrationSaga.Start(Run, Ticket, null, T0);
        saga.RecordError("push rejected", T0.AddMinutes(2));

        Assert.Equal("push rejected", saga.LastError);
        Assert.False(saga.IsCompleted);

        saga.AdvanceTo(IntegrationSagaCheckpoint.Completed, T0);

        Assert.True(saga.IsCompleted);
        Assert.Null(saga.LastError);
    }

    [Fact]
    public void stack_layer_is_a_draft_until_marked_ready()
    {
        PullStackLayer layer = NewLayer(position: 1);

        Assert.True(layer.IsDraft);

        layer.MarkReady(T0.AddMinutes(1));

        Assert.False(layer.IsDraft);
        Assert.Equal(T0.AddMinutes(1), layer.UpdatedAt);
    }

    [Fact]
    public void stack_layer_position_is_one_based_and_diff_verification_is_recorded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewLayer(position: 0));

        PullStackLayer layer = NewLayer(position: 2);
        layer.RecordVerifiedDiff(ShaB, T0);

        Assert.Equal(ShaB, layer.VerifiedDiffSha);
    }

    [Fact]
    public void finding_issuance_is_planned_then_records_the_created_issue()
    {
        var issuance = FindingIssuance.Plan(Run, new StepRunId("s1"), FindingAxis.Testing, new FindingFingerprint("Broken login"), T0);

        Assert.Equal(FindingIssuanceStatus.Planned, issuance.Status);
        Assert.Null(issuance.IssueNumber);

        issuance.RecordCreated(42, 9001, T0.AddMinutes(1));

        Assert.Equal(FindingIssuanceStatus.Created, issuance.Status);
        Assert.Equal(42, issuance.IssueNumber);
        Assert.Equal(9001, issuance.IssueDatabaseId);
    }

    [Fact]
    public void finding_issuance_cannot_be_created_twice_with_different_issues()
    {
        var issuance = FindingIssuance.Plan(Run, new StepRunId("s1"), FindingAxis.Specification, new FindingFingerprint("x"), T0);
        issuance.RecordCreated(42, 9001, T0);

        Assert.Throws<InvalidOperationException>(() => issuance.RecordCreated(43, 9002, T0));
    }

    [Fact]
    public void finding_issuance_recording_the_same_issue_again_is_idempotent()
    {
        var issuance = FindingIssuance.Plan(Run, new StepRunId("s1"), FindingAxis.Specification, new FindingFingerprint("x"), T0);
        issuance.RecordCreated(42, 9001, T0);

        issuance.RecordCreated(42, 9001, T0.AddMinutes(1));

        Assert.Equal(FindingIssuanceStatus.Created, issuance.Status);
    }

    [Fact]
    public void outbox_message_is_pending_until_dispatched()
    {
        var message = OutboxMessage.Create("ticket.integrated", "{}", T0);

        Assert.True(message.IsPending);

        message.MarkDispatched(T0.AddSeconds(1));

        Assert.False(message.IsPending);
        Assert.Equal(T0.AddSeconds(1), message.DispatchedAt);
    }

    [Fact]
    public void outbox_failures_are_counted_and_keep_the_message_pending()
    {
        var message = OutboxMessage.Create("ticket.integrated", "{}", T0);

        message.RecordFailure("bus down");
        message.RecordFailure("still down");

        Assert.Equal(2, message.Attempts);
        Assert.Equal("still down", message.LastError);
        Assert.True(message.IsPending);
    }

    [Fact]
    public void run_event_captures_scope_and_payload()
    {
        var runEvent = RunEvent.Create(Run, Ticket, "ticket.integrated", "{\"a\":1}", T0);

        Assert.Equal(Run, runEvent.SpecRunId);
        Assert.Equal(Ticket, runEvent.TicketRunId);
        Assert.Equal("{\"a\":1}", runEvent.PayloadJson);
        Assert.Equal(T0, runEvent.OccurredAt);
    }

    [Fact]
    public void test_lease_is_active_until_released_and_tracks_the_process()
    {
        var lease = TestLease.Acquire(Run, 5050, "/work/lease", T0, TimeSpan.FromMinutes(10));

        Assert.True(lease.IsActive);
        Assert.Equal(T0.AddMinutes(10), lease.ExpiresAt);

        lease.AttachProcess(1234);
        lease.Release(T0.AddMinutes(1));

        Assert.Equal(1234, lease.ProcessId);
        Assert.False(lease.IsActive);
        Assert.Equal(T0.AddMinutes(1), lease.ReleasedAt);
    }

    [Fact]
    public void test_lease_expires_only_while_unreleased()
    {
        var lease = TestLease.Acquire(Run, 5050, "/work/lease", T0, TimeSpan.FromMinutes(10));

        Assert.False(lease.IsExpiredAt(T0.AddMinutes(9)));
        Assert.True(lease.IsExpiredAt(T0.AddMinutes(10)));

        lease.Release(T0.AddMinutes(11));

        Assert.False(lease.IsExpiredAt(T0.AddMinutes(30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void test_lease_rejects_invalid_ports(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestLease.Acquire(Run, port, "/w", T0, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void repository_exposes_ref_and_can_be_disabled()
    {
        var repo = RepositoryRecord.Register(new GitHubRepoRef("o", "r"), new BranchName("main"), "https://x/o/r.git", "/repos/r", T0);

        Assert.Equal(new GitHubRepoRef("o", "r"), repo.Ref);

        repo.SetEnabled(true, T0.AddMinutes(1));

        Assert.True(repo.IsEnabled);
        Assert.Equal(T0.AddMinutes(1), repo.UpdatedAt);
    }

    [Fact]
    public void settings_profile_distinguishes_global_from_repository_scope()
    {
        Assert.True(SettingsProfile.ForGlobal().IsGlobal);
        Assert.Null(SettingsProfile.ForGlobal().RepositoryId);

        SettingsProfile repoProfile = SettingsProfile.ForRepository(7);

        Assert.False(repoProfile.IsGlobal);
        Assert.Equal(7, repoProfile.RepositoryId);
        Assert.Null(repoProfile.SpecDependencyMode);
    }

    [Fact]
    public void settings_profile_stores_per_role_overrides()
    {
        SettingsProfile profile = SettingsProfile.ForGlobal();

        profile.SetRole(AgentRole.Tester, new RoleSettingsOverride(Model: "m", TimeoutSeconds: 60));

        Assert.Equal("m", profile.Roles[AgentRole.Tester].Model);
        Assert.Equal(60, profile.Roles[AgentRole.Tester].TimeoutSeconds);
        Assert.False(profile.Roles.ContainsKey(AgentRole.Explorer));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(70000, 70001)]
    [InlineData(5100, 5000)]
    public void test_port_range_rejects_invalid_bounds(int start, int end)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestPortRange(start, end));
    }

    [Fact]
    public void test_port_range_contains_is_inclusive()
    {
        var range = new TestPortRange(5000, 5010);

        Assert.True(range.Contains(5000));
        Assert.True(range.Contains(5010));
        Assert.False(range.Contains(4999));
        Assert.False(range.Contains(5011));
    }

    private static PullStackLayer NewLayer(int position) =>
        PullStackLayer.Create(Run, Ticket, new BranchName("stack/run1/t1"), ShaA, new PullRequestNumber(10), new BranchName("main"), position, T0);
}
