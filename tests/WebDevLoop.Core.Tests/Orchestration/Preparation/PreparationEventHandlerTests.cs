using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Tests.Orchestration.Preparation;

public sealed class PreparationEventHandlerTests
{
    private readonly SpecWorkflowFixture _fixture = new();
    private readonly RecordingPreparationLauncher _launcher = new();

    private PreparationEventHandler Handler => new(_launcher, _fixture.Store, _fixture.Store);

    [Fact]
    public async Task a_spec_entering_preparing_is_prepared_in_the_background()
    {
        SpecRun run = await PreparingSpecAsync();

        await HandleAsync(new SpecRunStatusChanged(run.Id, run.RepositoryId, SpecRunStatus.Queued, SpecRunStatus.Preparing, _fixture.Clock.UtcNow));

        Assert.Equal([new PreparationAssignment(run.Id)], _launcher.Launched);
    }

    [Fact]
    public async Task reconciliation_relaunches_a_preparing_spec_nothing_works_on()
    {
        SpecRun run = await PreparingSpecAsync();

        await HandleAsync(new FrontierReconciliationRequested(run.Id, _fixture.Clock.UtcNow));

        Assert.Equal([new PreparationAssignment(run.Id)], _launcher.Launched);
    }

    [Fact]
    public async Task reconciliation_leaves_a_preparing_spec_with_a_running_explorer_alone()
    {
        SpecRun run = await PreparingSpecAsync();
        StepRun explorer = StepRun.Create(new StepRunId("explore"), run.Id, null, StepKind.Explore, AgentRole.Explorer, 1, "hash");
        explorer.Start(_fixture.Clock.UtcNow, TimeSpan.FromMinutes(30));
        ((IStepRunRepository)_fixture.Store).Add(explorer);

        await HandleAsync(new FrontierReconciliationRequested(run.Id, _fixture.Clock.UtcNow));

        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public async Task reconciliation_of_a_spec_past_preparation_launches_nothing()
    {
        SpecRun run = await PreparingSpecAsync();
        _fixture.Advance(run, SpecRunStatus.Running);

        await HandleAsync(new FrontierReconciliationRequested(run.Id, _fixture.Clock.UtcNow));
        await HandleAsync(new SpecRunStatusChanged(run.Id, run.RepositoryId, SpecRunStatus.Preparing, SpecRunStatus.Running, _fixture.Clock.UtcNow));

        Assert.Empty(_launcher.Launched);
    }

    private async Task<SpecRun> PreparingSpecAsync()
    {
        _fixture.SeedSpec(1);
        SpecRun run = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        Assert.Equal(SpecRunStatus.Preparing, run.Status);
        return run;
    }

    private Task HandleAsync(WorkflowEvent workflowEvent) =>
        Handler.HandleAsync(new EventEnvelope(1, workflowEvent), TestContext.Current.CancellationToken);

    private sealed class RecordingPreparationLauncher : IPreparationLauncher
    {
        public List<PreparationAssignment> Launched { get; } = [];

        public void Launch(PreparationAssignment assignment) => Launched.Add(assignment);
    }
}
