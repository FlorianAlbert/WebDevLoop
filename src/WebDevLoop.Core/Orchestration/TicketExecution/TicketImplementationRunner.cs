using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// Workflow step 4 for one claimed ticket (entry point for <see cref="IImplementationLauncher"/>): creates the ticket's
/// unique active implement step, creates/resets the run-scoped worktree on the integration tip and verifies its ancestry,
/// runs the implementer, and validates the report (branch head SHA, integration tip merged) before moving the ticket to
/// review. Failed agent turns are retried in a fresh session up to <c>MaxRetries</c>; the ticket keeps its implementer
/// slot meanwhile because it stays <c>Implementing</c>. An assignment from restart recovery
/// (<see cref="ImplementationAssignment.ResumeSessionId"/>) first resumes the interrupted session in its preserved worktree;
/// a missing session or worktree restarts the attempt from the integration tip in a fresh session on the same branch.
/// </summary>
public sealed class TicketImplementationRunner(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IEffectiveSettingsProvider settings,
    IGitWorkspace git,
    IAgentRunner agents,
    PromptRenderer prompts,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    TicketExecutionOptions options)
{
    private const AgentRole Role = AgentRole.Implementer;

    private const string ResumePreamble =
        "WebDevLoop restarted while you were working on this ticket. Your worktree is preserved: inspect what you already did, "
        + "continue where you left off, and finish with the report below.";

    private readonly TicketBranchVerifier _verifier = new(git);

    public async Task<ImplementationResult> RunAsync(ImplementationAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        TicketRun? ticket = await ticketRuns.GetAsync(assignment.TicketRunId, cancellationToken);
        if (ticket is not { Status: TicketRunStatus.Implementing })
        {
            return ImplementationResult.NotImplementing;
        }

        IReadOnlyList<StepRun> steps = await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken);
        if (steps.Any(step => step.IsActive && step.Kind.IsImplementOrFix()))
        {
            return ImplementationResult.AlreadyRunning;
        }

        if (await LoadContextAsync(ticket, cancellationToken) is not { } context)
        {
            return await NeedsAttentionAsync(
                ticket,
                ImplementationOutcome.Failed,
                AttentionReasons.InternalInconsistency($"Spec run or repository of ticket '{ticket.Id}' is missing.", forTicket: true),
                cancellationToken);
        }

        int firstAttempt = steps.Count(step => step.Kind == StepKind.Implement) + 1;
        int attempts = context.Settings.MaxRetries + 1;
        string? lastFailure = null;
        string? lastBlocked = null;
        AgentSessionId? resume = assignment.ResumeSessionId is { } interrupted && await IsResumableWorktreeAsync(context, cancellationToken)
            ? interrupted
            : null;
        for (int attempt = firstAttempt; attempt < firstAttempt + attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttemptResult result = await RunAttemptAsync(context, attempt, resume, cancellationToken);
            resume = null;
            if (result.Final is { } final)
            {
                return final;
            }

            lastFailure = result.RetryableFailure;
            lastBlocked = result.BlockedMessage;
        }

        string details = $"Implementation failed after {attempts} attempt(s): {lastFailure}";
        AttentionReason reason = lastBlocked is null
            ? AttentionReasons.ImplementationFailed(attempts, lastFailure!).WithDetails(details)
            : AttentionReasons.ImplementerBlocked(lastBlocked).WithDetails(details);
        return await NeedsAttentionAsync(ticket, ImplementationOutcome.Failed, reason, cancellationToken);
    }

    private async Task<ImplementationContext?> LoadContextAsync(TicketRun ticket, CancellationToken cancellationToken)
    {
        SpecRun? spec = await specRuns.GetAsync(ticket.SpecRunId, cancellationToken);
        RepositoryRecord? repository = spec is null ? null : await repositories.GetAsync(spec.RepositoryId, cancellationToken);
        if (spec is null || repository is null)
        {
            return null;
        }

        EffectiveSettings effective = await settings.GetAsync(spec.RepositoryId, cancellationToken);
        return new ImplementationContext(
            spec,
            ticket,
            repository,
            effective,
            GitRepositoryLocation.From(repository),
            RunWorkspaceLayout.For(effective.WorkspaceRootDirectory, spec.Id));
    }

    /// <summary>The interrupted implementer's worktree still exists on the ticket branch (uncommitted work is kept).</summary>
    private async Task<bool> IsResumableWorktreeAsync(ImplementationContext context, CancellationToken cancellationToken)
    {
        WorktreeInspection worktree = await git.InspectWorktreeAsync(context.Location, context.WorktreePath, cancellationToken);
        return worktree.Status is WorktreeStatus.Clean or WorktreeStatus.Dirty && worktree.Branch == context.Ticket.BranchName;
    }

    /// <param name="resume">The interrupted session to continue instead of starting from the integration tip.</param>
    private async Task<AttemptResult> RunAttemptAsync(ImplementationContext context, int attempt, AgentSessionId? resume, CancellationToken cancellationToken)
    {
        TicketRun ticket = context.Ticket;
        if (await CurrentIntegrationTipAsync(context, cancellationToken) is not { } integrationTip)
        {
            return AttemptResult.Finished(await NeedsAttentionAsync(
                ticket,
                ImplementationOutcome.Failed,
                AttentionReasons.InternalInconsistency($"Spec run '{context.Spec.Id}' has no integration tip.", forTicket: true),
                cancellationToken));
        }

        string prompt;
        try
        {
            prompt = await RenderPromptAsync(context, attempt, integrationTip, cancellationToken);
        }
        catch (Exception exception) when (exception is SettingsValidationException or PromptRenderingException)
        {
            return AttemptResult.Finished(await NeedsAttentionAsync(
                ticket,
                ImplementationOutcome.Failed,
                AttentionReasons.PromptNotRenderable("Implementer", $"The implementer prompt cannot be rendered: {exception.Message}"),
                cancellationToken));
        }

        StepRun step = StartStep(context, attempt, prompt, resume);
        if (!await SaveAsync(cancellationToken))
        {
            return AttemptResult.Finished(ImplementationResult.ConcurrencyConflict);
        }

        Verdict verdict;
        try
        {
            verdict = await RunStartedStepAsync(context, step, prompt, integrationTip, resume is not null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A step left running would hold the implementer slot and block every relaunch (AlreadyRunning).
            verdict = Verdict.Retryable(StepStatus.Failed, $"The implementer step failed unexpectedly: {exception.Message}", null);
        }

        if (verdict.Outcome is null)
        {
            FinishStep(step, verdict);
            return await SaveAsync(cancellationToken)
                ? AttemptResult.Retry(verdict.Failure!, verdict.BlockedMessage)
                : AttemptResult.Finished(ImplementationResult.ConcurrencyConflict);
        }

        return AttemptResult.Finished(await FinishAsync(context, step, verdict, cancellationToken));
    }

    /// <summary>
    /// Resumes the step's interrupted session, or resets the worktree onto <paramref name="integrationTip"/> and starts a
    /// fresh session (also when the resumed session no longer exists), then judges the implementer's report.
    /// </summary>
    private async Task<Verdict> RunStartedStepAsync(
        ImplementationContext context,
        StepRun step,
        string prompt,
        CommitSha integrationTip,
        bool resumeSession,
        CancellationToken cancellationToken)
    {
        if (resumeSession)
        {
            string resumePrompt = $"{ResumePreamble}{Environment.NewLine}{Environment.NewLine}{prompt}";
            AgentRunResult resumed = await agents.ResumeAsync(BuildRequest(context, step, resumePrompt), cancellationToken);
            if (resumed.Outcome != AgentRunOutcome.SessionNotFound)
            {
                return await JudgeAsync(context, resumed, integrationTip, cancellationToken);
            }

            step.CopilotSessionId = ids.NewAgentSessionId(step.Id).Value;
        }

        BranchName branch = context.Ticket.BranchName;
        await git.PrepareWorktreeAsync(context.Location, new WorktreeSpec(branch, integrationTip, context.WorktreePath), cancellationToken);
        if (await _verifier.VerifyWorktreeBaseAsync(context.Location, context.WorktreePath, branch, integrationTip, cancellationToken) is { } problem)
        {
            return new Verdict(StepStatus.Failed, ImplementationOutcome.Failed, problem.Details, null, Attention: problem);
        }

        AgentRunResult run = await agents.StartAsync(BuildRequest(context, step, prompt), cancellationToken);
        return await JudgeAsync(context, run, integrationTip, cancellationToken);
    }

    /// <summary>The local integration ref is updated before the run's recorded tip, so it is the freshest source.</summary>
    private async Task<CommitSha?> CurrentIntegrationTipAsync(ImplementationContext context, CancellationToken cancellationToken) =>
        await git.GetBranchTipAsync(context.Location, context.Spec.IntegrationBranch, GitRefScope.Local, cancellationToken)
        ?? context.Spec.IntegrationTipSha;

    private async Task<string> RenderPromptAsync(ImplementationContext context, int attempt, CommitSha integrationTip, CancellationToken cancellationToken)
    {
        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken);
        IReadOnlyList<TicketDependency> dependencies = await ticketRuns.ListDependenciesAsync(context.Spec.Id, cancellationToken);
        IReadOnlyDictionary<string, string> values = ImplementerPromptValues.Build(
            context, attempt, integrationTip, options.SkillsRoot, tickets, dependencies);
        return prompts.Render(Role, context.Settings.For(Role).PromptTemplate, values);
    }

    /// <summary>Adds the attempt's step as running; the filtered unique index rejects a second active implement step.</summary>
    private StepRun StartStep(ImplementationContext context, int attempt, string prompt, AgentSessionId? resume)
    {
        StepRun step = StepRun.Create(ids.NewStepRunId(), context.Spec.Id, context.Ticket.Id, StepKind.Implement, Role, attempt, Hash(prompt));
        step.CopilotSessionId = (resume ?? ids.NewAgentSessionId(step.Id)).Value;
        RoleSettings role = context.Settings.For(Role);
        step.RecordLaunchSettings(role.Model, role.ReasoningEffort);
        step.WorktreePath = context.WorktreePath;
        step.BranchName = context.Ticket.BranchName;
        step.Start(clock.UtcNow, context.Settings.For(Role).Timeout);
        context.Ticket.WorktreePath = context.WorktreePath;
        stepRuns.Add(step);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, clock.UtcNow));
        return step;
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
                    ImplementationOutcome.Implemented => new Verdict(StepStatus.Succeeded, ImplementationOutcome.Implemented, null, Serialize(report), head),
                    ImplementationOutcome.IntegrationMergeMissing => new Verdict(
                        StepStatus.NeedsAttention, verification.Outcome, verification.Reason, Serialize(report), Attention: verification.Attention),
                    _ => new Verdict(StepStatus.Failed, verification.Outcome, verification.Reason, Serialize(report), Attention: verification.Attention),
                };
            case { Report: ImplementationReport report }:
                return Verdict.Retryable(StepStatus.Failed, $"Implementer reported blocked: {report.Summary}", Serialize(report), report.Summary);
            case { Report: { } other }:
                return Verdict.Retryable(StepStatus.Failed, $"Implementer returned an unexpected {other.GetType().Name}.", null);
            case { Outcome: AgentRunOutcome.Cancelled }:
                return new Verdict(StepStatus.Cancelled, ImplementationOutcome.Cancelled, run.FailureReason, null);
            case { Outcome: AgentRunOutcome.TimedOut }:
                return Verdict.Retryable(StepStatus.TimedOut, run.FailureReason!, null);
            default:
                return Verdict.Retryable(StepStatus.Failed, run.FailureReason!, null);
        }
    }

    private async Task<ImplementationResult> FinishAsync(ImplementationContext context, StepRun step, Verdict verdict, CancellationToken cancellationToken)
    {
        FinishStep(step, verdict);
        ImplementationOutcome outcome = verdict.Outcome!.Value;
        switch (outcome)
        {
            case ImplementationOutcome.Implemented:
                DateTimeOffset now = clock.UtcNow;
                TicketRun ticket = context.Ticket;
                ticket.LastImplementedSha = verdict.ImplementedHead;
                ticket.TransitionTo(TicketRunStatus.Reviewing, now);
                outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, now));
                return await SaveAsync(cancellationToken) ? ImplementationResult.Implemented : ImplementationResult.ConcurrencyConflict;
            case ImplementationOutcome.Cancelled:
                return await SaveAsync(cancellationToken) ? new ImplementationResult(outcome, verdict.Failure) : ImplementationResult.ConcurrencyConflict;
            default:
                return await NeedsAttentionAsync(
                    context.Ticket, outcome, verdict.Attention ?? AttentionReasons.ImplementationFailed(1, verdict.Failure!).WithDetails(verdict.Failure!), cancellationToken);
        }
    }

    private void FinishStep(StepRun step, Verdict verdict)
    {
        DateTimeOffset now = clock.UtcNow;
        if (verdict.StepStatus == StepStatus.NeedsAttention)
        {
            step.MarkNeedsAttention(verdict.Attention!, now, verdict.ResultJson);
        }
        else
        {
            step.Finish(verdict.StepStatus, now, verdict.ResultJson, verdict.Failure);
        }

        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, now));
    }

    private async Task<ImplementationResult> NeedsAttentionAsync(
        TicketRun ticket,
        ImplementationOutcome outcome,
        AttentionReason reason,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRunStatus previous = ticket.Status;
        ticket.MarkNeedsAttention(reason, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, TicketRunStatus.NeedsAttention, now));
        return await SaveAsync(cancellationToken) ? new ImplementationResult(outcome, reason.Details) : ImplementationResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    private static string Serialize(ImplementationReport report) => JsonSerializer.Serialize(report);

    private static string Hash(string prompt) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));

    /// <param name="Outcome">Null when the attempt failed in a way a fresh attempt may fix.</param>
    /// <param name="ImplementedHead">The verified ticket branch head of a successful report.</param>
    private sealed record Verdict(
        StepStatus StepStatus,
        ImplementationOutcome? Outcome,
        string? Failure,
        string? ResultJson,
        CommitSha? ImplementedHead = null,
        AttentionReason? Attention = null,
        string? BlockedMessage = null)
    {
        public static Verdict Retryable(StepStatus status, string failure, string? resultJson, string? blockedMessage = null) =>
            new(status, null, failure, resultJson, BlockedMessage: blockedMessage);
    }

    private sealed record AttemptResult(ImplementationResult? Final, string? RetryableFailure, string? BlockedMessage = null)
    {
        public static AttemptResult Finished(ImplementationResult result) => new(result, null);

        public static AttemptResult Retry(string failure, string? blockedMessage) => new(null, failure, blockedMessage);
    }
}
