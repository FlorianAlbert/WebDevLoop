using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.SpecQueue;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.Startup;

public sealed class SpecQueueRecomputationTests
{
    private const int OtherRepositoryId = 2;

    private readonly SpecWorkflowFixture _fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
    public async Task a_spec_recovery_parked_in_needs_attention_frees_its_slot_for_the_next_queued_spec()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        SpecRun parked = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        parked.MarkNeedsAttention("Implementer interrupted too often.", _fixture.Clock.UtcNow);
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
        return new SpecQueueScheduler(repositories ?? _fixture.Store, _fixture.Store, _fixture.Issues, settings, _fixture.Store, _fixture.Store, _fixture.Clock);
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
        _fixture.Issues.Seed(new IssueRef(repository.Owner, repository.Name, specNumber), $"Spec {specNumber}");
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
