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

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// Step two of the resolution order, after the known remediation and before the user: one bounded <see cref="AgentRole.Troubleshooter"/>
/// session for a ticket that needs attention, for the reason codes <see cref="TroubleshooterEscalation"/> lets through.
/// It is bounded by the role's timeout, by the "max attempts" setting per code since the user's last Retry, and by a state
/// fingerprint: a code in an unchanged git state is never escalated twice, so the stage cannot loop. The session is a normal
/// persisted <see cref="StepKind.Troubleshoot"/> step (aborting the run cancels it). Everything the agent may change is backed
/// up first, and a claim that the problem is solved is verified by <see cref="TroubleshooterVerifier"/> before the phase resumes.
/// When it fails, its diagnosis travels in the result and ends up on the "Action needed" card.
/// </summary>
public sealed class TroubleshooterStage(
    AttentionWorkLoader loader,
    TroubleshootingStateReader reader,
    TroubleshooterWorkspace workspace,
    TroubleshooterVerifier verifier,
    IAgentRunner agents,
    PromptRenderer prompts,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IRunEventRepository runEvents,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    TroubleshooterOptions options) : IAttentionStage
{
    private const AgentRole Role = AgentRole.Troubleshooter;
    private const string Unknown = "(unknown)";

    public string Name => TroubleshooterRunEvents.StageName;

    public async Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attentionCase);
        if (!attentionCase.IsTicket
            || TroubleshooterEscalation.For(attentionCase.Reason.Code) is not { Escalates: true, Check: { } check }
            || await loader.LoadAsync(attentionCase, cancellationToken) is not { Ticket: { } } work
            || !work.Settings.TroubleshooterEnabled)
        {
            return AttentionStageResult.NotApplicable;
        }

        IReadOnlyList<RunEvent> events = await runEvents.ListBySpecRunAsync(attentionCase.SpecRunId, cancellationToken);
        int limit = work.Settings.TroubleshooterMaxAttempts;
        int started = TroubleshooterRunEvents.CountStarted(events, attentionCase);
        if (started >= limit)
        {
            return AttentionStageResult.Unresolved(
                $"The troubleshooter already ran {started} time(s) without solving it.",
                $"Troubleshooter: ran {started} time(s) (the limit is {limit}) without solving it.");
        }

        TroubleshootingState state = await reader.ReadAsync(work, attentionCase, cancellationToken);
        if (TroubleshooterRunEvents.WasEscalated(events, attentionCase, state.Fingerprint))
        {
            return Unchanged(TroubleshooterRunEvents.FindDiagnosis(events, attentionCase, state.Fingerprint));
        }

        int attempt = (await stepRuns.ListByTicketRunAsync(attentionCase.TicketRunId!.Value, cancellationToken)).Count(step => step.Kind == StepKind.Troubleshoot) + 1;
        TroubleshooterWorkspaceInfo? info = await workspace.PrepareAsync(work, attentionCase, state, attempt, cancellationToken);
        if (info is null)
        {
            return AttentionStageResult.Unresolved("The troubleshooter could not start: the ticket branch does not exist locally.", "Troubleshooter: could not start because the ticket branch is missing.");
        }

        try
        {
            return await RunSessionAsync(work, attentionCase, check, state, info, attempt, cancellationToken);
        }
        finally
        {
            await workspace.ReleaseAsync(work, info, CancellationToken.None);
        }
    }

    private static AttentionStageResult Unchanged(TroubleshooterFinding? previous)
    {
        const string summary = "The troubleshooter already looked at this problem in this exact state, so it is not asked again.";
        AttentionStageResult result = AttentionStageResult.Unresolved(summary, "Troubleshooter: already looked at this problem in an unchanged state; it is not asked again.");
        return previous is null ? result : result with { Diagnosis = previous.ToDiagnosis() };
    }

    private async Task<AttentionStageResult> RunSessionAsync(
        AttentionWork work,
        AttentionCase attentionCase,
        TroubleshooterCheck check,
        TroubleshootingState state,
        TroubleshooterWorkspaceInfo info,
        int attempt,
        CancellationToken cancellationToken)
    {
        string prompt;
        try
        {
            prompt = await RenderPromptAsync(work, attentionCase, state, info, attempt, cancellationToken);
        }
        catch (Exception exception) when (exception is SettingsValidationException or PromptRenderingException)
        {
            return AttentionStageResult.Unresolved(
                $"The troubleshooter prompt cannot be rendered: {exception.Message}", $"Troubleshooter: its prompt cannot be rendered ({exception.Message}).");
        }

        StepRun step = StartStep(work, attempt, prompt);
        runEvents.Add(TroubleshooterRunEvents.Start(attentionCase, state.Fingerprint, step.Id, attempt, clock.UtcNow));
        if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AttentionStageResult.Unresolved("The run changed while the troubleshooter was starting.", "Troubleshooter: the run changed while it was starting.");
        }

        AgentRunResult run;
        try
        {
            run = await agents.StartAsync(BuildRequest(work, step, prompt, info), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A step left running would never end: the run page would show the troubleshooter working forever.
            return await FinishAsync(step, attentionCase, state, StepStatus.Failed, null, $"The troubleshooter session failed unexpectedly: {exception.Message}", null, false, "Failed", cancellationToken);
        }

        return run switch
        {
            { Report: TroubleshooterReport report } => await JudgeAsync(work, attentionCase, check, state, step, report, cancellationToken),
            { Report: { } other } => await FinishAsync(step, attentionCase, state, StepStatus.Failed, null, $"The troubleshooter returned an unexpected {other.GetType().Name}.", null, false, "Failed", cancellationToken),
            { Outcome: AgentRunOutcome.Cancelled } => await FinishAsync(step, attentionCase, state, StepStatus.Cancelled, null, "The troubleshooter session was cancelled.", null, false, "Cancelled", cancellationToken),
            { Outcome: AgentRunOutcome.TimedOut } => await FinishAsync(step, attentionCase, state, StepStatus.TimedOut, null, $"The troubleshooter timed out: {run.FailureReason}", null, false, "TimedOut", cancellationToken),
            _ => await FinishAsync(step, attentionCase, state, StepStatus.Failed, null, $"The troubleshooter failed: {run.FailureReason}", null, false, "Failed", cancellationToken),
        };
    }

    private async Task<AttentionStageResult> JudgeAsync(
        AttentionWork work,
        AttentionCase attentionCase,
        TroubleshooterCheck check,
        TroubleshootingState state,
        StepRun step,
        TroubleshooterReport report,
        CancellationToken cancellationToken)
    {
        string resultJson = JsonSerializer.Serialize(report);
        var diagnosis = new AttentionStageDiagnosis(
            new AttentionDiagnosis(Name, report.Summary, report.ActionsTaken), report.UserSteps, report.SuggestedButtons);
        if (report.Outcome != TroubleshooterOutcome.Resolved)
        {
            string tried = report.Outcome == TroubleshooterOutcome.NeedsUser
                ? "Troubleshooter: investigated the problem and found that it needs you."
                : "Troubleshooter: investigated the problem but could not resolve it.";
            return await FinishAsync(
                step, attentionCase, state, StepStatus.Succeeded, resultJson, null, diagnosis, false, report.Outcome.ToString(), cancellationToken, report.Summary, tried: tried);
        }

        TroubleshooterVerdict verdict = await verifier.VerifyAsync(work, check, state, cancellationToken);
        if (!verdict.Verified)
        {
            string rejected = $"The troubleshooter reported the problem as solved, but WebDevLoop's own check disagreed: {verdict.Explanation}";
            return await FinishAsync(
                step, attentionCase, state, StepStatus.Failed, resultJson, rejected, diagnosis, false, "ClaimRejected", cancellationToken, rejected, tried: $"Troubleshooter: {rejected}");
        }

        string summary = $"Troubleshooter: {Sentence(report.Summary)} WebDevLoop verified it: {verdict.Explanation}";
        return await FinishAsync(step, attentionCase, state, StepStatus.Succeeded, resultJson, null, null, true, "Resolved", cancellationToken, summary, verdict.Resume);
    }

    /// <summary>Ends the step durably and records what the session found; the step is left alone when a control command already cancelled it.</summary>
    private async Task<AttentionStageResult> FinishAsync(
        StepRun started,
        AttentionCase attentionCase,
        TroubleshootingState state,
        StepStatus status,
        string? resultJson,
        string? failureReason,
        AttentionStageDiagnosis? diagnosis,
        bool verified,
        string outcome,
        CancellationToken cancellationToken,
        string? summary = null,
        AttentionResume? resume = null,
        string? tried = null)
    {
        string text = summary ?? failureReason ?? "The troubleshooter did not solve the problem.";
        if (await stepRuns.GetAsync(started.Id, cancellationToken) is not { Status: StepStatus.Running } step)
        {
            return AttentionStageResult.Unresolved("The troubleshooter session was stopped before it finished.", "Troubleshooter: the session was stopped before it finished.");
        }

        DateTimeOffset now = clock.UtcNow;
        step.Finish(status, now, resultJson, failureReason);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, now));
        runEvents.Add(TroubleshooterRunEvents.Finish(attentionCase, state.Fingerprint, step.Id, outcome, verified, text, diagnosis, now));
        if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AttentionStageResult.Unresolved("The run changed while the troubleshooter worked.", "Troubleshooter: the run changed while it worked.");
        }

        if (resume is not null)
        {
            return AttentionStageResult.Resolved(text, resume);
        }

        return AttentionStageResult.Unresolved(text, tried ?? $"Troubleshooter: {failureReason ?? text}") with { Diagnosis = diagnosis };
    }

    private StepRun StartStep(AttentionWork work, int attempt, string prompt)
    {
        TicketRun ticket = work.Ticket!;
        StepRun step = StepRun.Create(ids.NewStepRunId(), work.Spec.Id, ticket.Id, StepKind.Troubleshoot, Role, attempt, Hash(prompt));
        step.CopilotSessionId = ids.NewAgentSessionId(step.Id).Value;
        step.WorktreePath = work.TicketWorktreePath;
        step.BranchName = ticket.BranchName;
        RoleSettings role = work.Settings.For(Role);
        step.RecordLaunchSettings(role.Model, role.ReasoningEffort);
        step.Start(clock.UtcNow, role.Timeout);
        stepRuns.Add(step);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, clock.UtcNow));
        return step;
    }

    private static AgentRunRequest BuildRequest(AttentionWork work, StepRun step, string prompt, TroubleshooterWorkspaceInfo info)
    {
        RoleSettings role = work.Settings.For(Role);
        string[] protectedBranches = [work.Spec.IntegrationBranch.Value, (work.Spec.BaseBranch ?? work.Settings.BaseBranch).Value];
        return new AgentRunRequest(
            step.Id,
            new AgentSessionId(step.CopilotSessionId!),
            work.Repository.Ref,
            new AgentModelSettings(role.Model, role.ReasoningEffort, role.Timeout),
            prompt,
            RoleCapabilityPolicies.For(Role, info.ToAgentWorkspace(protectedBranches)));
    }

    private async Task<string> RenderPromptAsync(
        AttentionWork work,
        AttentionCase attentionCase,
        TroubleshootingState state,
        TroubleshooterWorkspaceInfo info,
        int attempt,
        CancellationToken cancellationToken)
    {
        TicketRun ticket = work.Ticket!;
        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(work.Spec.Id, cancellationToken);
        IReadOnlyList<TicketDependency> dependencies = await ticketRuns.ListDependenciesAsync(work.Spec.Id, cancellationToken);
        CommitSha? tip = state.IntegrationTip ?? work.Spec.IntegrationTipSha;
        Dictionary<string, string> values = SpecPromptValues.ForSpec(
            work.Spec, work.Repository, work.Settings, work.Layout, attempt, tip ?? default, options.SkillsRoot, tickets, dependencies);
        values[PromptPlaceholders.IntegrationTipSha] = tip?.Value ?? Unknown;
        SpecPromptValues.AddTicket(values, ticket, tickets, dependencies);
        values[PromptPlaceholders.WorktreePath] = info.TicketWorktree;
        values[PromptPlaceholders.BranchName] = ticket.BranchName.Value;
        values[PromptPlaceholders.IntegrationWorktreePath] = info.IntegrationWorktree ?? "(not available: the integration branch does not exist locally)";
        values[PromptPlaceholders.AttentionCode] = attentionCase.Reason.Code.ToString();
        values[PromptPlaceholders.AttentionSummary] = attentionCase.Reason.Summary;
        values[PromptPlaceholders.AttentionDetails] = attentionCase.Reason.Details;
        values[PromptPlaceholders.FailedPhase] = ticket.NeedsAttentionFrom?.ToString() ?? "(unknown)";
        values[PromptPlaceholders.RemediationTried] = attentionCase.Reason.TriedSoFar.Count == 0
            ? "(nothing so far)"
            : string.Join('\n', attentionCase.Reason.TriedSoFar.Select(tried => "- " + tried));
        values[PromptPlaceholders.GitState] = state.GitText;
        values[PromptPlaceholders.GitHubState] = state.GitHubText;
        values[PromptPlaceholders.RecentAgentLogs] = info.LogTail;
        values[PromptPlaceholders.TroubleshootingContextPath] = info.ContextDirectory;
        values[PromptPlaceholders.BackupPath] = info.BackupDirectory;
        return prompts.Render(Role, work.Settings.For(Role).PromptTemplate, values);
    }

    private static string Sentence(string text)
    {
        string line = text.Trim();
        return line.EndsWith('.') || line.EndsWith('!') || line.EndsWith('?') ? line : line + ".";
    }

    private static string Hash(string prompt) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
}
