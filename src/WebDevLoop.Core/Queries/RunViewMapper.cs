using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

public static class RunViewMapper
{
    public static SpecRunView ToView(this SpecRun run) => new(
        run.Id.Value,
        run.RepositoryId,
        run.ParentIssue.Number,
        run.Title,
        run.Status,
        run.QueuePosition,
        run.BaseBranch?.Value,
        run.IntegrationBranch.Value,
        run.IntegrationBaseSha?.Value,
        run.IntegrationTipSha?.Value,
        run.DependencyModeUsed,
        run.ReviewCycle,
        run.TestCycle,
        run.CreatedAt,
        run.StartedAt,
        run.ReadyAt,
        run.CompletedAt,
        run.FailureReason,
        run.NeedsAttentionFrom,
        run.Status == SpecRunStatus.NeedsAttention ? run.Attention ?? AttentionReasons.Unclassified(run.FailureReason, forTicket: false) : null);

    public static TicketRunView ToView(this TicketRun ticket, IEnumerable<TicketRunId> blockedBy) => new(
        ticket.Id.Value,
        ticket.SpecRunId.Value,
        ticket.Issue.Number,
        ticket.Title,
        ticket.Status,
        ticket.Attempt,
        ticket.ReviewIteration,
        ticket.BranchName.Value,
        ticket.WorktreePath,
        ticket.LastImplementedSha?.Value,
        ticket.IntegratedCommitSha?.Value,
        ticket.PullRequestNumber?.Value,
        ticket.StackPosition,
        blockedBy.Select(id => id.Value).Order(StringComparer.Ordinal).ToArray(),
        ticket.CreatedAt,
        ticket.UpdatedAt,
        ticket.FailureReason,
        ticket.NeedsAttentionFrom,
        ticket.Status == TicketRunStatus.NeedsAttention ? ticket.Attention ?? AttentionReasons.Unclassified(ticket.FailureReason, forTicket: true) : null);

    public static StepRunView ToView(this StepRun step) => new(
        step.Id.Value,
        step.SpecRunId.Value,
        step.TicketRunId?.Value,
        step.Kind,
        step.AgentRole,
        step.Status,
        step.Attempt,
        step.CopilotSessionId,
        step.WorktreePath,
        step.BranchName?.Value,
        step.StartedAt,
        step.CompletedAt,
        step.TimeoutAt,
        step.InputPromptHash,
        step.StructuredResultJson,
        step.FailureReason,
        step.Model,
        step.ReasoningEffort,
        step.Attention);

    public static RunEventView ToView(this RunEvent runEvent) => new(
        runEvent.Id,
        runEvent.SpecRunId.Value,
        runEvent.TicketRunId?.Value,
        runEvent.Type,
        runEvent.PayloadJson,
        runEvent.OccurredAt);

    public static StackLayerView ToView(this PullStackLayer layer) => new(
        layer.Position,
        layer.TicketRunId.Value,
        layer.BranchName.Value,
        layer.BaseBranch.Value,
        layer.CommitSha.Value,
        layer.PullRequestNumber.Value,
        layer.StackNumber,
        layer.IsDraft,
        layer.VerifiedDiffSha?.Value);

    public static IntegrationSagaView ToView(this IntegrationSaga saga) => new(
        saga.TicketRunId.Value,
        saga.Checkpoint,
        saga.StackBranchName.Value,
        saga.PullRequestNumber?.Value,
        saga.LastError,
        saga.UpdatedAt);
}
