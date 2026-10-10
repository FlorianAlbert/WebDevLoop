using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Workflow step 6 for one ticket: sends the review findings to the implementer and validates the fix. The fix turn takes
/// an implementer slot exactly like an initial implementation (same <see cref="ImplementerCapacityGate"/> and
/// <see cref="ImplementerCapacity"/> check, then a compare-and-swap claim <c>Reviewing → FixingReviewFindings</c>). The first
/// turn resumes the original implementer session (or, if that session no longer exists, starts a fresh one in the same
/// turn); failed turns are retried in a fresh session up to <c>MaxRetries</c>.
/// </summary>
public sealed class ReviewFixRunner(
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IRunEventRepository runEvents,
    IGitWorkspace git,
    IAgentRunner agents,
    PromptRenderer prompts,
    ImplementerCapacity capacity,
    ImplementerCapacityGate gate,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    TicketExecutionOptions options)
{
    private const AgentRole Role = AgentRole.Implementer;

    private readonly TicketBranchVerifier _verifier = new(git);
    private readonly WorktreeRemediator _remediator = new(git, runEvents, clock);
    private readonly ReviewLoopJournal _journal = new(stepRuns, outbox, clock);

    /// <param name="context">The ticket must be <c>Reviewing</c> with a validated <see cref="TicketRun.LastImplementedSha"/>.</param>
    internal async Task<FixResult> RunAsync(ImplementationContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        IReadOnlyList<StepRun> steps = await stepRuns.ListByTicketRunAsync(context.Ticket.Id, cancellationToken);
        if (await ClaimSlotAsync(context, cancellationToken) is { } notClaimed)
        {
            return notClaimed;
        }

        return await RunTurnsAsync(context, steps, findings, OriginalImplementerSession(steps), cancellationToken);
    }

    /// <summary>
    /// Continues an interrupted fix of a ticket that still holds its slot (<c>FixingReviewFindings</c>): the first turn
    /// resumes <paramref name="interruptedSession"/>, or the original implementer session when none is given.
    /// </summary>
    internal async Task<FixResult> ResumeAsync(
        ImplementationContext context,
        IReadOnlyList<Finding> findings,
        AgentSessionId? interruptedSession,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StepRun> steps = await stepRuns.ListByTicketRunAsync(context.Ticket.Id, cancellationToken);
        return await RunTurnsAsync(context, steps, findings, interruptedSession ?? OriginalImplementerSession(steps), cancellationToken);
    }

    private async Task<FixResult> RunTurnsAsync(
        ImplementationContext context,
        IReadOnlyList<StepRun> steps,
        IReadOnlyList<Finding> findings,
        AgentSessionId? originalSession,
        CancellationToken cancellationToken)
    {
        TicketRun ticket = context.Ticket;
        string findingsJson = ReviewFindingsJson.Render(findings);
        int firstAttempt = steps.Count(step => step.Kind == StepKind.Fix) + 1;
        int attempts = context.Settings.MaxRetries + 1;
        string? lastFailure = null;
        string? lastBlocked = null;
        for (int turn = 0; turn < attempts; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AgentSessionId? resumable = turn == 0 ? originalSession : null;
            TurnResult result = await RunTurnAsync(context, firstAttempt + turn, resumable, findingsJson, cancellationToken);
            if (result.Final is { } final)
            {
                return final;
            }

            lastFailure = result.RetryableFailure;
            lastBlocked = result.BlockedMessage;
        }

        string details = $"Fixing the review findings failed after {attempts} attempt(s): {lastFailure}";
        AttentionReason reason = lastBlocked is null
            ? AttentionReasons.FixFailed(attempts, lastFailure!).WithDetails(details)
            : AttentionReasons.ImplementerBlocked(lastBlocked, fix: true).WithDetails(details);
        return await NeedsAttentionAsync(ticket, reason, cancellationToken);
    }

    /// <returns>Null when the ticket now holds an implementer slot; otherwise why not.</returns>
    private async Task<FixResult?> ClaimSlotAsync(ImplementationContext context, CancellationToken cancellationToken)
    {
        using (await gate.EnterAsync(cancellationToken))
        {
            if (await capacity.GetAvailableAsync(context.Spec.RepositoryId, context.Settings, cancellationToken) <= 0)
            {
                return FixResult.NoImplementerCapacity;
            }

            _journal.Move(context.Ticket, TicketRunStatus.FixingReviewFindings);
            return await SaveAsync(cancellationToken) ? null : FixResult.ConcurrencyConflict;
        }
    }

    /// <summary>The session of the implementation that was reviewed: the latest succeeded implement step of the ticket.</summary>
    private static AgentSessionId? OriginalImplementerSession(IReadOnlyList<StepRun> steps) =>
        steps.Where(step => step.Kind == StepKind.Implement && step.Status == StepStatus.Succeeded)
            .MaxBy(step => step.Attempt)?.CopilotSessionId is { } session
            ? new AgentSessionId(session)
            : null;

    private async Task<TurnResult> RunTurnAsync(
        ImplementationContext context,
        int attempt,
        AgentSessionId? resumable,
        string findingsJson,
        CancellationToken cancellationToken)
    {
        TicketRun ticket = context.Ticket;
        if (await CurrentIntegrationTipAsync(context, cancellationToken) is not { } integrationTip)
        {
            return TurnResult.Finished(await NeedsAttentionAsync(
                ticket, AttentionReasons.InternalInconsistency($"Spec run '{context.Spec.Id}' has no integration tip.", forTicket: true), cancellationToken));
        }

        string prompt;
        try
        {
            prompt = await RenderPromptAsync(context, attempt, integrationTip, findingsJson, cancellationToken);
        }
        catch (Exception exception) when (exception is SettingsValidationException or PromptRenderingException)
        {
            return TurnResult.Finished(await NeedsAttentionAsync(
                ticket, AttentionReasons.PromptNotRenderable("Implementer", $"The fix prompt cannot be rendered: {exception.Message}"), cancellationToken));
        }

        StepRun step = StartStep(context, attempt, resumable, prompt);
        if (!await SaveAsync(cancellationToken))
        {
            return TurnResult.Finished(FixResult.ConcurrencyConflict);
        }

        Verdict verdict;
        try
        {
            verdict = await RunStartedTurnAsync(context, step, resumable, prompt, integrationTip, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A step left running would hold the implementer slot and block every relaunch (AlreadyRunning).
            verdict = Verdict.Retryable(StepStatus.Failed, $"The fix turn failed unexpectedly: {exception.Message}", null);
        }

        if (verdict.Outcome is null)
        {
            _journal.Finish(step, verdict.StepStatus, verdict.ResultJson, verdict.Failure);
            return await SaveAsync(cancellationToken) ? TurnResult.Retry(verdict.Failure!, verdict.BlockedMessage) : TurnResult.Finished(FixResult.ConcurrencyConflict);
        }

        return TurnResult.Finished(await FinishAsync(context, step, verdict, cancellationToken));
    }

    /// <summary>Checks the worktree, runs (or resumes) the implementer with the findings, and judges its report.</summary>
    private async Task<Verdict> RunStartedTurnAsync(
        ImplementationContext context,
        StepRun step,
        AgentSessionId? resumable,
        string prompt,
        CommitSha integrationTip,
        CancellationToken cancellationToken)
    {
        if (await VerifyWorktreeAsync(context, cancellationToken) is { } problem)
        {
            return new Verdict(StepStatus.Failed, FixOutcome.Failed, problem.Details, null, Attention: problem);
        }

        AgentRunRequest request = BuildRequest(context, step, prompt);
        AgentRunResult run = resumable is null
            ? await agents.StartAsync(request, cancellationToken)
            : await agents.ResumeAsync(request, cancellationToken);
        if (run.Outcome == AgentRunOutcome.SessionNotFound)
        {
            // The resumed session's state is gone; the findings prompt is self-contained, so a fresh session can fix them.
            step.CopilotSessionId = ids.NewAgentSessionId(step.Id).Value;
            run = await agents.StartAsync(BuildRequest(context, step, prompt), cancellationToken);
        }

        return await JudgeAsync(context, run, integrationTip, cancellationToken);
    }

    private async Task<CommitSha?> CurrentIntegrationTipAsync(ImplementationContext context, CancellationToken cancellationToken) =>
        await git.GetBranchTipAsync(context.Location, context.Spec.IntegrationBranch, GitRefScope.Local, cancellationToken)
        ?? context.Spec.IntegrationTipSha;

    private async Task<string> RenderPromptAsync(
        ImplementationContext context,
        int attempt,
        CommitSha integrationTip,
        string findingsJson,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken);
        IReadOnlyList<TicketDependency> dependencies = await ticketRuns.ListDependenciesAsync(context.Spec.Id, cancellationToken);
        IReadOnlyDictionary<string, string> values = ImplementerPromptValues.Build(
            context, attempt, integrationTip, options.SkillsRoot, tickets, dependencies, findingsJson, context.Ticket.ReviewIteration);
        return prompts.Render(Role, context.Settings.For(Role).PromptTemplate, values);
    }

    /// <summary>Adds the fix step as running; the filtered unique index rejects a second active implement/fix step.</summary>
    private StepRun StartStep(ImplementationContext context, int attempt, AgentSessionId? resumable, string prompt)
    {
        StepRun step = StepRun.Create(ids.NewStepRunId(), context.Spec.Id, context.Ticket.Id, StepKind.Fix, Role, attempt, Hash(prompt));
        step.CopilotSessionId = (resumable ?? ids.NewAgentSessionId(step.Id)).Value;
        step.WorktreePath = context.WorktreePath;
        step.BranchName = context.Ticket.BranchName;
        RoleSettings role = context.Settings.For(Role);
        step.RecordLaunchSettings(role.Model, role.ReasoningEffort);
        _journal.Start(step, role.Timeout);
        return step;
    }

    /// <returns>Null when the worktree is a clean checkout of the ticket branch containing the reviewed commit.</returns>
    private async Task<AttentionReason?> VerifyWorktreeAsync(ImplementationContext context, CancellationToken cancellationToken)
    {
        BranchName branch = context.Ticket.BranchName;
        CommitSha reviewed = context.Ticket.LastImplementedSha!.Value;
        await _remediator.RemediateAsync(context.Spec.Id, context.Ticket.Id, context.Location, context.Layout, context.WorktreePath, cancellationToken);
        WorktreeInspection worktree = await git.InspectWorktreeAsync(context.Location, context.WorktreePath, cancellationToken);
        bool usable = worktree is { Status: WorktreeStatus.Clean, Head: { } head }
            && worktree.Branch == branch
            && await git.IsAncestorAsync(context.Location, reviewed, head, cancellationToken);
        return usable
            ? null
            : AttentionReasons.WorktreeNotClean(
                context.WorktreePath,
                branch.Value,
                $"Worktree '{context.WorktreePath}' is {worktree.Status} on '{worktree.Branch}' at {worktree.Head?.Value ?? "(missing)"} "
                + $"instead of a clean checkout of '{branch}' containing the reviewed commit {reviewed}.")
                .WithTried("Saved uncommitted changes as a patch, reset the working folder and removed untracked files; it is still not a clean checkout of the reviewed commit.");
    }

    private static AgentRunRequest BuildRequest(ImplementationContext context, StepRun step, string prompt)
    {
        RoleSettings role = context.Settings.For(Role);
        return new AgentRunRequest(
            step.Id,
            new AgentSessionId(step.CopilotSessionId!),
            context.Repository.Ref,
            new AgentModelSettings(role.Model, role.ReasoningEffort, role.Timeout),
            prompt,
            RoleCapabilityPolicies.For(Role, new AgentWorkspace(context.WorktreePath, context.Layout.ExplorationNotesDirectory)));
    }

    /// <param name="startTip">
    /// The integration tip the agent was given. The report must contain it; a tip that another ticket's integration moved
    /// on since is not required, because the integration saga squashes onto the current tip (resolving conflicts) anyway.
    /// </param>
    private async Task<Verdict> JudgeAsync(ImplementationContext context, AgentRunResult run, CommitSha startTip, CancellationToken cancellationToken)
    {
        switch (run)
        {
            case { Report: ImplementationReport { Status: ReportStatus.Completed, HeadCommitSha: { } head } report }:
                ReportVerification verification = await _verifier.VerifyReportAsync(
                    context.Location, context.WorktreePath, context.Ticket.BranchName, head, startTip, cancellationToken);
                return verification.Outcome switch
                {
                    ImplementationOutcome.Implemented => new Verdict(StepStatus.Succeeded, FixOutcome.Fixed, null, Serialize(report), head),
                    ImplementationOutcome.IntegrationMergeMissing => new Verdict(
                        StepStatus.NeedsAttention, FixOutcome.Failed, verification.Reason, Serialize(report), Attention: verification.Attention),
                    _ => new Verdict(StepStatus.Failed, FixOutcome.Failed, verification.Reason, Serialize(report), Attention: verification.Attention),
                };
            case { Report: ImplementationReport report }:
                return Verdict.Retryable(StepStatus.Failed, $"Implementer reported blocked: {report.Summary}", Serialize(report), report.Summary);
            case { Report: { } other }:
                return Verdict.Retryable(StepStatus.Failed, $"Implementer returned an unexpected {other.GetType().Name}.", null);
            case { Outcome: AgentRunOutcome.Cancelled }:
                return new Verdict(StepStatus.Cancelled, FixOutcome.Cancelled, run.FailureReason, null);
            case { Outcome: AgentRunOutcome.TimedOut }:
                return Verdict.Retryable(StepStatus.TimedOut, run.FailureReason!, null);
            default:
                return Verdict.Retryable(StepStatus.Failed, run.FailureReason!, null);
        }
    }

    private async Task<FixResult> FinishAsync(ImplementationContext context, StepRun step, Verdict verdict, CancellationToken cancellationToken)
    {
        _journal.Finish(step, verdict.StepStatus, verdict.ResultJson, verdict.Failure, verdict.Attention);
        switch (verdict.Outcome!.Value)
        {
            case FixOutcome.Fixed:
                context.Ticket.LastImplementedSha = verdict.FixedHead;
                _journal.Move(context.Ticket, TicketRunStatus.Reviewing);
                return await SaveAsync(cancellationToken) ? FixResult.Fixed : FixResult.ConcurrencyConflict;
            case FixOutcome.Cancelled:
                return await SaveAsync(cancellationToken) ? new FixResult(FixOutcome.Cancelled, verdict.Failure) : FixResult.ConcurrencyConflict;
            default:
                return await NeedsAttentionAsync(
                    context.Ticket, verdict.Attention ?? AttentionReasons.FixFailed(1, verdict.Failure!).WithDetails(verdict.Failure!), cancellationToken);
        }
    }

    private async Task<FixResult> NeedsAttentionAsync(TicketRun ticket, AttentionReason reason, CancellationToken cancellationToken)
    {
        _journal.MarkNeedsAttention(ticket, reason);
        return await SaveAsync(cancellationToken) ? new FixResult(FixOutcome.Failed, reason.Details) : FixResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    private static string Serialize(ImplementationReport report) => JsonSerializer.Serialize(report);

    private static string Hash(string prompt) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));

    /// <param name="Outcome">Null when the turn failed in a way a fresh session may fix.</param>
    /// <param name="FixedHead">The verified ticket branch head of a successful fix report.</param>
    private sealed record Verdict(
        StepStatus StepStatus,
        FixOutcome? Outcome,
        string? Failure,
        string? ResultJson,
        CommitSha? FixedHead = null,
        AttentionReason? Attention = null,
        string? BlockedMessage = null)
    {
        public static Verdict Retryable(StepStatus status, string failure, string? resultJson, string? blockedMessage = null) =>
            new(status, null, failure, resultJson, BlockedMessage: blockedMessage);
    }

    private sealed record TurnResult(FixResult? Final, string? RetryableFailure, string? BlockedMessage = null)
    {
        public static TurnResult Finished(FixResult result) => new(result, null);

        public static TurnResult Retry(string failure, string? blockedMessage) => new(null, failure, blockedMessage);
    }
}
