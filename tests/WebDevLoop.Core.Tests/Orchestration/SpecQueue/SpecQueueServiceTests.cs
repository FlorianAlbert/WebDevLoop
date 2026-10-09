using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.SpecQueue;

public sealed class SpecQueueServiceTests
{
    private readonly SpecWorkflowFixture _fixture = new();

    private ISpecRunRepository SpecRuns => _fixture.Store;

    [Fact]
    public async Task enqueue_snapshots_the_spec_issue_in_queue_order_and_appends_a_queued_event()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);

        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await _fixture.EnqueueAsync(2);

        Assert.Equal(SpecRunStatus.Queued, first.Status);
        Assert.Equal(SpecWorkflowFixture.Issue(1), first.ParentIssue);
        Assert.Equal("Spec 1", first.Title);
        Assert.Equal("body of Spec 1", first.BodySnapshot);
        Assert.True(second.QueuePosition > first.QueuePosition);
        Assert.Equal(
            [first.Id, second.Id],
            _fixture.Store.PendingEvents.OfType<SpecRunQueued>().Select(queued => queued.SpecRunId));
    }

    [Fact]
    public async Task enqueue_loads_native_spec_dependencies_onto_queued_runs_or_external_issues()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(5);
        _fixture.SeedSpec(2, 1, 5);
        SpecRun blocker = await _fixture.EnqueueAsync(1);

        SpecRun dependent = await _fixture.EnqueueAsync(2);

        IReadOnlyList<SpecDependency> dependencies = await SpecRuns.ListDependenciesAsync(dependent.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, dependencies.Count);
        Assert.Contains(dependencies, dependency => dependency.BlockingSpecRunId == blocker.Id && dependency.ExternalBlockingIssue is null);
        Assert.Contains(dependencies, dependency => dependency.BlockingSpecRunId is null && dependency.ExternalBlockingIssue?.Number == 5);
        Assert.All(dependencies, dependency => Assert.Equal(DependencySource.GitHub, dependency.Source));
    }

    [Fact]
    public async Task enqueueing_a_spec_that_already_has_an_unfinished_run_returns_that_run()
    {
        _fixture.SeedSpec(1);
        SpecRun existing = await _fixture.EnqueueAsync(1);

        EnqueueResult again = await _fixture.Queue.EnqueueAsync(_fixture.Repository.Id, 1, TestContext.Current.CancellationToken);

        Assert.Equal(EnqueueOutcome.AlreadyQueued, again.Outcome);
        Assert.Equal(existing.Id, again.SpecRunId);
        Assert.Single(await SpecRuns.ListByRepositoryAsync(_fixture.Repository.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task enqueue_for_an_unknown_repository_is_rejected()
    {
        EnqueueResult result = await _fixture.Queue.EnqueueAsync(42, 1, TestContext.Current.CancellationToken);

        Assert.Equal(EnqueueOutcome.RepositoryNotFound, result.Outcome);
        Assert.Null(result.SpecRunId);
    }
}
