using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

/// <summary>The integration saga parks a ticket with the reason code that matches what went wrong.</summary>
public sealed class IntegrationAttentionReasonTests
{
    private readonly IntegrationFixture _f = new();

    [Fact]
    public async Task a_fault_that_is_not_temporary_is_not_offered_the_automatic_retry()
    {
        _f.Settings.Defaults = _f.Settings.Defaults with { MaxRetries = 0 };
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");
        _f.JournaledPulls.FailNextCreate = new InvalidOperationException("Resource not accessible by integration");

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Equal(AttentionCode.IntegrationFailed, ticket.Attention!.Code);
        Assert.Null(ticket.Attention.AutoFix);
        Assert.Contains("Resource not accessible", ticket.Attention.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_temporary_fault_offers_the_automatic_retry_with_backoff()
    {
        _f.Settings.Defaults = _f.Settings.Defaults with { MaxRetries = 0 };
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");
        _f.JournaledPulls.FailNextCreate = new HttpRequestException("502 Bad Gateway");

        await _f.IntegrateAsync(ticket);

        Assert.Equal(AttentionCode.IntegrationTemporaryFailure, ticket.Attention!.Code);
        Assert.True(ticket.Attention.AutoFixPending);
        Assert.Equal(AttentionCause.WebDevLoop, ticket.Attention.Cause);
    }

    [Fact]
    public async Task a_reviewed_branch_that_adds_nothing_is_parked_with_the_no_changes_reason()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");
        _f.Git.NoChangesOnNextSquash = true;

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Equal(AttentionCode.TicketHasNoChanges, ticket.Attention!.Code);
        Assert.True(ticket.Attention.AutoFixPending);
        Assert.Contains("no changes relative to the integration tip", ticket.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task an_integration_branch_that_moved_on_the_remote_is_parked_as_rejected_push_for_the_user()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");
        _f.Git.SeedRemoteBranch(spec.IntegrationBranch, "someone-elses.txt");

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Equal(AttentionCode.IntegrationPushRejected, ticket.Attention!.Code);
        Assert.Equal(AttentionCause.You, ticket.Attention.Cause);
        Assert.Contains(ticket.Attention.UserSteps, step => step.Command is not null && step.Command.Contains(spec.IntegrationBranch.Value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task a_stack_branch_that_already_exists_on_the_remote_is_parked_with_the_command_to_delete_it()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");
        _f.Git.SeedRemoteBranch(RunScopedNaming.StackBranch(spec.Id, ticket.Id), "old.txt");

        await _f.IntegrateAsync(ticket);

        Assert.Equal(AttentionCode.StackBranchExists, ticket.Attention!.Code);
        Assert.Contains($"git push origin --delete {RunScopedNaming.StackBranch(spec.Id, ticket.Id)}", ticket.Attention.UserSteps.Select(step => step.Command));
    }
}
