using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.SpecQueue;

public sealed class SpecQueueEventHandlerTests
{
    private readonly SpecWorkflowFixture _fixture = new();

    public SpecQueueEventHandlerTests()
    {
        _fixture.SeedTicket(11, spec: 1);
        _fixture.SeedTicket(12, spec: 2);
    }

    private SpecQueueEventHandler Handler => new(_fixture.Scheduler);

    [Fact]
    public async Task a_label_lookup_failure_reaches_the_event_bus_diagnostics_without_starting_work()
    {
        _fixture.SeedSpec(1);
        SpecRun run = await _fixture.EnqueueAsync(1);
        _fixture.Labels.LookupFailures[SpecWorkflowFixture.Repo] = new RepositoryLabelException("Check Issues permissions and retry.");

        GitHubIssuesException error = await Assert.ThrowsAsync<GitHubIssuesException>(() =>
            HandleAsync(new SpecRunQueued(run.Id, _fixture.Repository.Id, _fixture.Clock.UtcNow)));

        Assert.Contains("Check Issues permissions and retry.", error.Message);
        Assert.Equal(SpecRunStatus.Queued, run.Status);
    }

    [Fact]
    public async Task a_queued_spec_is_scheduled_right_away()
    {
        _fixture.SeedSpec(1);
        SpecRun run = await _fixture.EnqueueAsync(1);

        await HandleAsync(new SpecRunQueued(run.Id, _fixture.Repository.Id, _fixture.Clock.UtcNow));

        Assert.Equal(SpecRunStatus.Preparing, run.Status);
    }

    [Theory]
    [InlineData(SpecRunStatus.AwaitingMerge)]
    [InlineData(SpecRunStatus.NeedsAttention)]
    [InlineData(SpecRunStatus.Completed)]
    public async Task a_spec_leaving_its_active_slot_starts_the_next_queued_spec(SpecRunStatus to)
    {
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(first, ToStatus(to));

        await HandleAsync(new SpecRunStatusChanged(first.Id, _fixture.Repository.Id, SpecRunStatus.Testing, to, _fixture.Clock.UtcNow));

        Assert.Equal(SpecRunStatus.Preparing, second.Status);
    }

    [Theory]
    [InlineData(SpecRunStatus.Preparing)]
    [InlineData(SpecRunStatus.WaitingForDependency)]
    [InlineData(SpecRunStatus.Running)]
    public async Task scheduling_and_active_transitions_do_not_trigger_another_pass(SpecRunStatus to)
    {
        _fixture.SeedSpec(1);
        SpecRun queued = await _fixture.EnqueueAsync(1);

        await HandleAsync(new SpecRunStatusChanged(new RunId("other"), _fixture.Repository.Id, SpecRunStatus.Queued, to, _fixture.Clock.UtcNow));

        Assert.Equal(SpecRunStatus.Queued, queued.Status);
    }

    private static SpecRunStatus[] ToStatus(SpecRunStatus to) => to switch
    {
        SpecRunStatus.NeedsAttention => [SpecRunStatus.NeedsAttention],
        SpecRunStatus.AwaitingMerge => [SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge],
        _ => [SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge, SpecRunStatus.Completed],
    };

    private Task HandleAsync(WorkflowEvent workflowEvent) =>
        Handler.HandleAsync(new EventEnvelope(1, workflowEvent), TestContext.Current.CancellationToken);
}
