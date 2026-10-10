using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

/// <summary>Unexpected exceptions after the implement step started must not leave the step running or the slot taken.</summary>
public sealed class ImplementationFailureTests
{
    private readonly TicketExecutionFixture _fixture = new();

    private static CancellationToken Token => TicketExecutionFixture.Token;

    [Fact]
    public async Task agent_exception_finishes_the_step_as_failed_and_a_fresh_attempt_can_succeed()
    {
        SeededSpec spec = await SeedImplementingAsync();
        int turns = 0;
        _fixture.Agents.AutoReply = request => ++turns == 1
            ? throw new HttpRequestException("Copilot runtime unavailable.")
            : TicketExecutionFixture.Completed(_fixture.CommitInWorktree(request));

        ImplementationResult result = await RunAsync(spec);

        Assert.Equal(ImplementationResult.Implemented, result);
        IReadOnlyList<StepRun> steps = _fixture.Steps(spec[1]);
        Assert.Equal([(1, StepStatus.Failed), (2, StepStatus.Succeeded)], steps.Select(step => (step.Attempt, step.Status)));
        Assert.Contains("Copilot runtime unavailable.", steps[0].FailureReason, StringComparison.Ordinal);
        Assert.Equal(2, _fixture.Agents.Started.Select(request => request.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task agent_exceptions_on_every_attempt_free_the_implementer_slot_through_needs_attention()
    {
        _fixture.Settings.Defaults = _fixture.Settings.Defaults with { MaxRetries = 1 };
        SeededSpec spec = await SeedImplementingAsync();
        _fixture.Agents.AutoReply = _ => throw new HttpRequestException("Copilot runtime unavailable.");

        ImplementationResult result = await RunAsync(spec);

        Assert.Equal(ImplementationOutcome.Failed, result.Outcome);
        Assert.Contains("Copilot runtime unavailable.", result.Reason, StringComparison.Ordinal);
        Assert.Equal([StepStatus.Failed, StepStatus.Failed], _fixture.Steps(spec[1]).Select(step => step.Status));
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(AttentionCode.ImplementationFailed, _fixture.Ticket(spec[1]).Attention!.Code);
    }

    private async Task<SeededSpec> SeedImplementingAsync()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.MoveAsync(spec[1], TicketRunStatus.Ready, TicketRunStatus.Implementing);
        return spec;
    }

    private Task<ImplementationResult> RunAsync(SeededSpec spec) =>
        _fixture.Runner().RunAsync(new ImplementationAssignment(spec.Id, spec[1]), Token);
}
