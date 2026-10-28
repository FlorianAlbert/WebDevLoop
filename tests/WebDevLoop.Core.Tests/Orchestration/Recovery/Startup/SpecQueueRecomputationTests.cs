using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.SpecQueue;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.Startup;

public sealed class SpecQueueRecomputationTests
{
    private const int OtherRepositoryId = 2;

    private readonly SpecWorkflowFixture _fixture = new();

    public SpecQueueRecomputationTests()
    {
        _fixture.SeedTicket(11, spec: 1);
        _fixture.SeedTicket(12, spec: 2);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task recovery_rechecks_parked_work_fails_closed_and_resumes_only_in_a_free_slot_without_repreparing(bool missingParent)
    {
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        _fixture.SeedSpec(7);
        _fixture.Issues.SetBlockedBy(SpecWorkflowFixture.Issue(11), SpecWorkflowFixture.Issue(7));
        SpecRun parked = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        await _fixture.PrepareAsync(parked);
        parked.TransitionTo(SpecRunStatus.WaitingForTickets, _fixture.Clock.UtcNow);
        parked.SetWorkDecisionReason("All eligible tickets have open native blockers.");
        CommitSha? originalBase = parked.IntegrationBaseSha;
        CommitSha? originalTip = parked.IntegrationTipSha;
        DateTimeOffset? originalStart = parked.StartedAt;
        IssueRef missing = SpecWorkflowFixture.Issue(missingParent ? 1 : 7);
        _fixture.Issues.RemoveIssue(missing);
        SpecRun independent = await _fixture.EnqueueAsync(2);

        QueueRecomputationReport deferred = await Recomputation().RecomputeAsync(Token);

        Assert.Equal([independent.Id], Assert.Single(deferred.Repositories).Schedule.Activated);
        Assert.Contains("unavailable", Assert.Single(deferred.Faults).Error);
        Assert.Contains("unavailable", parked.WorkDecisionReason);
        Assert.Equal(SpecRunStatus.WaitingForTickets, parked.Status);
        Assert.Null(parked.MaxActiveSpecsSlot);

        _fixture.SeedSpec(1);
        _fixture.SeedSpec(7);
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(7), IssueCloseReason.Completed, Token);
        QueueRecomputationReport occupied = await Recomputation().RecomputeAsync(Token);
        Assert.Empty(Assert.Single(occupied.Repositories).Schedule.Activated);
        Assert.Empty(occupied.Faults);
        Assert.Equal(SpecRunStatus.WaitingForTickets, parked.Status);
        Assert.Contains("free active-spec slot", parked.WorkDecisionReason);

        _fixture.Advance(independent, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, SpecRunStatus.ReadyForReview);
        _fixture.GlobalSettings.BaseBranch = new BranchName("changed-trunk");
        QueueRecomputationReport resumed = await Recomputation().RecomputeAsync(Token);

        Assert.Equal([parked.Id], Assert.Single(resumed.Repositories).Schedule.Activated);
        Assert.Equal(SpecRunStatus.Running, parked.Status);
        Assert.Equal(1, parked.MaxActiveSpecsSlot);
        Assert.Equal(SpecWorkflowFixture.Trunk, parked.BaseBranch);
        Assert.Equal(originalBase, parked.IntegrationBaseSha);
        Assert.Equal(originalTip, parked.IntegrationTipSha);
        Assert.Equal(originalStart, parked.StartedAt);
        Assert.Null(parked.WorkDecisionReason);
        Assert.Empty((await Recomputation().RecomputeAsync(Token)).Faults);
        Assert.Single(_fixture.Store.PendingEvents.OfType<Core.Events.SpecRunStatusChanged>(),
            changed => changed.SpecRunId == parked.Id && changed.From == SpecRunStatus.WaitingForTickets && changed.To == SpecRunStatus.Running);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task unavailable_parent_or_blocker_is_an_actionable_recoverable_queue_fault(bool missingParent)
    {
        _fixture.SeedSpec(1, 7);
        _fixture.SeedSpec(7);
        SpecRun run = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        _fixture.Issues.RemoveIssue(SpecWorkflowFixture.Issue(missingParent ? 1 : 7));

        QueueRecomputationReport deferred = await Recomputation().RecomputeAsync(Token);

        Assert.Contains("Check issue access", Assert.Single(deferred.Faults).Error);
        Assert.Contains(run.Id.Value, Assert.Single(deferred.Faults).Error);
        Assert.Equal(run.Id, Assert.Single(Assert.Single(deferred.Repositories).Schedule.Faults).SpecRunId);
        Assert.Equal(SpecRunStatus.WaitingForDependency, run.Status);
        Assert.Null(run.MaxActiveSpecsSlot);
        _fixture.SeedSpec(1, 7);
        _fixture.SeedSpec(7);
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(7), IssueCloseReason.Completed, Token);

        QueueRecomputationReport recovered = await Recomputation().RecomputeAsync(Token);

        Assert.Empty(recovered.Faults);
        Assert.Equal([run.Id], Assert.Single(recovered.Repositories).Schedule.Activated);
    }

    [Fact]
    public async Task recomputation_schedules_the_queue_of_every_enabled_repository_and_skips_disabled_ones()
    {
        RepositoryRecord other = AddRepository("other", OtherRepositoryId, enabled: true);
        RepositoryRecord disabled = AddRepository("disabled", 3, enabled: false);
        _fixture.SeedSpec(1);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await EnqueueAsync(other, 2);
        SpecRun parked = await EnqueueAsync(disabled, 3);

        QueueRecomputationReport report = await Recomputation().RecomputeAsync(Token);

        Assert.Equal((SpecRunStatus.Preparing, SpecRunStatus.Preparing, SpecRunStatus.Queued), (first.Status, second.Status, parked.Status));
        Assert.Equal([_fixture.Repository.Id, OtherRepositoryId], report.Repositories.Select(repository => repository.RepositoryId));
        Assert.Equal([[first.Id], [second.Id]], report.Repositories.Select(repository => repository.Schedule.Activated));
        Assert.Empty(report.Faults);
    }

    [Fact]
    public async Task a_failing_repository_is_reported_and_does_not_stop_the_queue_of_another_repository()
    {
        RepositoryRecord other = AddRepository("other", OtherRepositoryId, enabled: true);
        _fixture.SeedSpec(1);
        await _fixture.EnqueueAsync(1);
        SpecRun second = await EnqueueAsync(other, 2);
        var failing = new FailingRepositoryLookup(_fixture.Store, _fixture.Repository.Id);

        QueueRecomputationReport report = await Recomputation(failing).RecomputeAsync(Token);

        QueueRecomputationFault fault = Assert.Single(report.Faults);
        Assert.Equal((_fixture.Repository.Id, FailingRepositoryLookup.Error), (fault.RepositoryId, fault.Error));
        Assert.Equal(SpecRunStatus.Preparing, second.Status);
        Assert.Equal([OtherRepositoryId], report.Repositories.Select(repository => repository.RepositoryId));
    }

    [Fact]
    public async Task a_label_lookup_failure_is_reported_without_holding_up_another_repository()
    {
        RepositoryRecord other = AddRepository("other", OtherRepositoryId, enabled: true);
        _fixture.SeedSpec(1);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await EnqueueAsync(other, 2);
        _fixture.Labels.LookupFailures[SpecWorkflowFixture.Repo] = new RepositoryLabelException("Check Issues permissions and retry.");

        QueueRecomputationReport report = await Recomputation().RecomputeAsync(Token);

        QueueRecomputationFault fault = Assert.Single(report.Faults);
        Assert.Equal(_fixture.Repository.Id, fault.RepositoryId);
        Assert.Contains("Check Issues permissions and retry.", fault.Error);
        Assert.Equal(SpecRunStatus.Queued, first.Status);
        Assert.Equal(SpecRunStatus.Preparing, second.Status);
        Assert.Equal([OtherRepositoryId], report.Repositories.Select(repository => repository.RepositoryId));
    }

    [Fact]
    public async Task a_spec_recovery_parked_in_needs_attention_frees_its_slot_for_the_next_queued_spec()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        SpecRun parked = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        parked.MarkNeedsAttention(AttentionReasons.Unclassified("Implementer interrupted too often.", true), _fixture.Clock.UtcNow);
        SpecRun next = await _fixture.EnqueueAsync(2);

        QueueRecomputationReport report = await Recomputation().RecomputeAsync(Token);

        Assert.Null(parked.MaxActiveSpecsSlot);
        Assert.Equal((SpecRunStatus.Preparing, 1), (next.Status, next.MaxActiveSpecsSlot));
        Assert.Equal([next.Id], Assert.Single(report.Repositories).Schedule.Activated);
        Assert.Null(await Scheduler().FindFreeSlotAsync(_fixture.Repository.Id, Token));
    }

    private SpecQueueRecomputation Recomputation(IRepositoryRecordRepository? repositories = null) =>
        new(_fixture.Store, Scheduler(repositories));

    private SpecQueueScheduler Scheduler(IRepositoryRecordRepository? repositories = null)
    {
        var settings = new PersistedEffectiveSettingsProvider(_fixture.Store, new SettingsResolver(TestSettings.EmbeddedDefaults()));
        return new SpecQueueScheduler(repositories ?? _fixture.Store, _fixture.Store, _fixture.Issues, settings, _fixture.Store, _fixture.Store, _fixture.Clock, new RepositoryOptIn(_fixture.Labels), _fixture.Store);
    }

    private RepositoryRecord AddRepository(string name, int id, bool enabled)
    {
        RepositoryRecord repository = RepositoryRecord.Register(
            new GitHubRepoRef(SpecWorkflowFixture.Repo.Owner, name), SpecWorkflowFixture.Trunk, $"https://github.com/octo/{name}.git", $"/work/repos/octo/{name}", SpecWorkflowFixture.T0);
        repository.SetEnabled(enabled, SpecWorkflowFixture.T0);
        typeof(RepositoryRecord).GetProperty(nameof(RepositoryRecord.Id))!.SetValue(repository, id);
        _fixture.Store.Add(repository);
        return repository;
    }

    private async Task<SpecRun> EnqueueAsync(RepositoryRecord repository, int specNumber)
    {
        var parent = new IssueRef(repository.Owner, repository.Name, specNumber);
        _fixture.Issues.Seed(parent, $"Spec {specNumber}");
        _fixture.Issues.Seed(new IssueRef(repository.Owner, repository.Name, 100 + specNumber), "Eligible ticket", parent);
        EnqueueResult result = await _fixture.Queue.EnqueueAsync(repository.Id, specNumber, Token);
        Assert.Equal(EnqueueOutcome.Queued, result.Outcome);
        return await _fixture.RunAsync(result.SpecRunId!.Value);
    }

    /// <summary>Loading one repository fails (e.g. its database row is corrupt); listing still works.</summary>
    private sealed class FailingRepositoryLookup(IRepositoryRecordRepository inner, int failingId) : IRepositoryRecordRepository
    {
        public const string Error = "Repository row is unreadable.";

        public Task<RepositoryRecord?> GetAsync(int id, CancellationToken cancellationToken) =>
            id == failingId ? throw new InvalidOperationException(Error) : inner.GetAsync(id, cancellationToken);

        public Task<RepositoryRecord?> FindAsync(GitHubRepoRef repo, CancellationToken cancellationToken) => inner.FindAsync(repo, cancellationToken);

        public Task<IReadOnlyList<RepositoryRecord>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

        public void Add(RepositoryRecord repository) => inner.Add(repository);

        public void Remove(RepositoryRecord repository) => inner.Remove(repository);
    }
}
