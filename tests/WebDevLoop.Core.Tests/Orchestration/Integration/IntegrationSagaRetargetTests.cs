using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class IntegrationSagaRetargetTests
{
    private static readonly DateTimeOffset T0 = IntegrationFixture.T0;
    private static readonly CommitSha OldTip = new(new string('1', 40));
    private static readonly CommitSha NewTip = new(new string('2', 40));

    [Fact]
    public void Retargeting_before_the_integration_ref_moved_restarts_the_squash_on_the_new_tip()
    {
        IntegrationSaga saga = IntegrationSaga.Start(new RunId("run1"), new TicketRunId("t1"), OldTip, T0);
        saga.SquashCommitSha = new CommitSha(new string('3', 40));
        saga.AdvanceTo(IntegrationSagaCheckpoint.SquashCommitCreated, T0);
        saga.RecordError("tip moved", T0);

        saga.RetargetTo(NewTip, T0.AddMinutes(1));

        Assert.Equal((NewTip, (CommitSha?)null, IntegrationSagaCheckpoint.Started, (string?)null),
            (saga.ExpectedPriorIntegrationSha!.Value, saga.SquashCommitSha, saga.Checkpoint, saga.LastError));
        Assert.Equal(T0.AddMinutes(1), saga.UpdatedAt);
    }

    [Fact]
    public void Retargeting_after_the_integration_ref_moved_is_rejected()
    {
        IntegrationSaga saga = IntegrationSaga.Start(new RunId("run1"), new TicketRunId("t1"), OldTip, T0);
        saga.AdvanceTo(IntegrationSagaCheckpoint.IntegrationRefUpdated, T0);

        Assert.Throws<InvalidStatusTransitionException>(() => saga.RetargetTo(NewTip, T0));
        Assert.Equal(OldTip, saga.ExpectedPriorIntegrationSha);
    }
}
