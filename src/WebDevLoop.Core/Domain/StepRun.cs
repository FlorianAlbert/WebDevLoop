namespace WebDevLoop.Core.Domain;

public sealed class StepRun : VersionedEntity
{
    private StepRun()
    {
        InputPromptHash = string.Empty;
    }

    public StepRunId Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public TicketRunId? TicketRunId { get; private set; }

    public StepKind Kind { get; private set; }

    public AgentRole? AgentRole { get; private set; }

    public StepStatus Status { get; private set; }

    public int Attempt { get; private set; }

    public string? CopilotSessionId { get; set; }

    /// <summary>The model the agent session was launched with; null for steps recorded before it was captured.</summary>
    public string? Model { get; private set; }

    public string? ReasoningEffort { get; private set; }

    public string? WorktreePath { get; set; }

    public BranchName? BranchName { get; set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? TimeoutAt { get; private set; }

    public string InputPromptHash { get; private set; }

    public string? StructuredResultJson { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Structured guidance when the step ended in <see cref="StepStatus.NeedsAttention"/>.</summary>
    public AttentionReason? Attention { get; private set; }

    public bool IsActive => Status.IsActive();

    public static StepRun Create(
        StepRunId id,
        RunId specRunId,
        TicketRunId? ticketRunId,
        StepKind kind,
        AgentRole? agentRole,
        int attempt,
        string inputPromptHash) => new()
        {
            Id = id,
            SpecRunId = specRunId,
            TicketRunId = ticketRunId,
            Kind = kind,
            AgentRole = agentRole,
            Attempt = attempt,
            InputPromptHash = inputPromptHash,
        };

    public void RecordLaunchSettings(string model, string reasoningEffort)
    {
        Model = model;
        ReasoningEffort = reasoningEffort;
    }

    public void Start(DateTimeOffset at, TimeSpan timeout)
    {
        TransitionTo(StepStatus.Running);
        StartedAt = at;
        TimeoutAt = at + timeout;
    }

    public void Finish(StepStatus outcome, DateTimeOffset at, string? structuredResultJson = null, string? failureReason = null)
    {
        if (outcome == StepStatus.NeedsAttention)
        {
            throw new ArgumentException("A step needing attention must be finished with MarkNeedsAttention so it carries a reason.", nameof(outcome));
        }

        TransitionTo(outcome);
        CompletedAt = at;
        StructuredResultJson = structuredResultJson;
        FailureReason = failureReason;
    }

    /// <summary>Ends the running step as <see cref="StepStatus.NeedsAttention"/>; the reason is mandatory.</summary>
    public void MarkNeedsAttention(AttentionReason reason, DateTimeOffset at, string? structuredResultJson = null)
    {
        ArgumentNullException.ThrowIfNull(reason);
        TransitionTo(StepStatus.NeedsAttention);
        CompletedAt = at;
        StructuredResultJson = structuredResultJson;
        FailureReason = reason.Details;
        Attention = reason;
    }

    private void TransitionTo(StepStatus next)
    {
        if (!Status.CanTransitionTo(next))
        {
            throw new InvalidStatusTransitionException(nameof(StepRun), Status, next);
        }

        Status = next;
    }
}
