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

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Runs a <see cref="AgentRole.ConflictResolver"/> session when, and only when, squash-merging a ticket conflicts. The agent
/// works in the ticket's own worktree under the conflict-resolver policy (edits confined to that worktree, no GitHub
/// token, no push/PR/stack commands) and merges the integration tip into the ticket branch locally. The app verifies the
/// reported head (branch tip, worktree HEAD, contains the integration tip) before the saga retries the squash. Each
/// resolution is a persisted <see cref="StepKind.ResolveConflict"/> step; at most <c>MaxRetries + 1</c> are attempted per ticket.
/// </summary>
public sealed class ConflictResolutionRunner(
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IGitWorkspace git,
    IAgentRunner agents,
    PromptRenderer prompts,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    IntegrationOptions options)
{
    private const AgentRole Role = AgentRole.ConflictResolver;
    private const string NoChangedFiles = "(none)";

    private readonly TicketBranchVerifier _verifier = new(git);

    /// <param name="integrationTip">The tip the squash conflicted with; the resolved branch must contain it.</param>
    internal async Task<ConflictResolution> ResolveAsync(
        IntegrationContext context,
        CommitSha integrationTip,
        IReadOnlyList<string> conflictingFiles,
        IReadOnlyList<string> changedFiles,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StepRun> steps = await stepRuns.ListByTicketRunAsync(context.Ticket.Id, cancellationToken);
        int attempt = steps.Count(step => step.Kind == StepKind.ResolveConflict) + 1;
        int maxAttempts = context.Settings.MaxRetries + 1;
        if (attempt > maxAttempts)
        {
            return ConflictResolution.Failed(
                $"Squash-merging still conflicts in {string.Join(", ", conflictingFiles)} after {maxAttempts} conflict resolution attempt(s).");
        }

        if (await EnsureWorktreeAsync(context, cancellationToken) is { } problem)
        {
            return ConflictResolution.Failed(problem);
        }

        string prompt;
        try
        {
            prompt = await RenderPromptAsync(context, attempt, integrationTip, conflictingFiles, changedFiles, cancellationToken);
        }
        catch (Exception exception) when (exception is SettingsValidationException or PromptRenderingException)
        {
            return ConflictResolution.Failed($"The conflict resolver prompt cannot be rendered: {exception.Message}");
        }

        StepRun step = StartStep(context, attempt, prompt);
        if (!await SaveAsync(cancellationToken))
        {
            return ConflictResolution.ConcurrencyConflict;
        }

        Verdict verdict;
        try
        {
            AgentRunResult run = await agents.StartAsync(BuildRequest(context, step, prompt), cancellationToken);
            verdict = await JudgeAsync(context, run, integrationTip, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A step left running would block every later saga run (AlreadyRunning); the saga records the fault itself.
            ConflictResolution failed = ConflictResolution.Failed($"The conflict resolver turn failed unexpectedly: {exception.Message}");
            FinishStep(step, new Verdict(StepStatus.Failed, failed, null));
            await SaveAsync(cancellationToken);
            throw;
        }

        FinishStep(step, verdict);
        if (verdict.ResolvedHead is { } head)
        {
            context.Ticket.LastImplementedSha = head;
        }

        return await SaveAsync(cancellationToken) ? verdict.Resolution : ConflictResolution.ConcurrencyConflict;
    }

    private void FinishStep(StepRun step, Verdict verdict)
    {
        DateTimeOffset now = clock.UtcNow;
        step.Finish(verdict.StepStatus, now, verdict.ResultJson, verdict.Resolution.Reason);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, now));
    }

    /// <returns>Null when the worktree is a clean checkout of the ticket branch at the reviewed commit; otherwise the problem.</returns>
    private async Task<string?> EnsureWorktreeAsync(IntegrationContext context, CancellationToken cancellationToken)
    {
        TicketRun ticket = context.Ticket;
        CommitSha reviewed = ticket.LastImplementedSha!.Value;
        WorktreeInspection worktree = await git.InspectWorktreeAsync(context.Location, context.WorktreePath, cancellationToken);
        if (worktree.Status == WorktreeStatus.Missing)
        {
            await git.PrepareWorktreeAsync(context.Location, new WorktreeSpec(ticket.BranchName, reviewed, context.WorktreePath), cancellationToken);
            worktree = await git.InspectWorktreeAsync(context.Location, context.WorktreePath, cancellationToken);
        }

        return worktree.Status == WorktreeStatus.Clean && worktree.Branch == ticket.BranchName && worktree.Head == reviewed
            ? null
            : $"Worktree '{context.WorktreePath}' is {worktree.Status} on '{worktree.Branch}' at {worktree.Head?.Value ?? "(missing)"} "
              + $"instead of a clean checkout of '{ticket.BranchName}' at the reviewed commit {reviewed}.";
    }

    private async Task<string> RenderPromptAsync(
        IntegrationContext context,
        int attempt,
        CommitSha integrationTip,
        IReadOnlyList<string> conflictingFiles,
        IReadOnlyList<string> changedFiles,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken);
        IReadOnlyList<TicketDependency> dependencies = await ticketRuns.ListDependenciesAsync(context.Spec.Id, cancellationToken);
        Dictionary<string, string> values = SpecPromptValues.ForSpec(
            context.Spec, context.Repository, context.Settings, context.Layout, attempt, integrationTip, options.SkillsRoot, tickets, dependencies);
        SpecPromptValues.AddTicket(values, context.Ticket, tickets, dependencies);
        values[PromptPlaceholders.WorktreePath] = context.WorktreePath;
        values[PromptPlaceholders.BranchName] = context.Ticket.BranchName.Value;
        values[PromptPlaceholders.ConflictingFiles] = string.Join('\n', conflictingFiles);
        values[PromptPlaceholders.ChangedFiles] = changedFiles.Count == 0 ? NoChangedFiles : string.Join('\n', changedFiles);
        return prompts.Render(Role, context.Settings.For(Role).PromptTemplate, values);
    }

    private StepRun StartStep(IntegrationContext context, int attempt, string prompt)
    {
        StepRun step = StepRun.Create(ids.NewStepRunId(), context.Spec.Id, context.Ticket.Id, StepKind.ResolveConflict, Role, attempt, Hash(prompt));
        step.CopilotSessionId = ids.NewAgentSessionId(step.Id).Value;
        step.WorktreePath = context.WorktreePath;
        step.BranchName = context.Ticket.BranchName;
        RoleSettings role = context.Settings.For(Role);
        step.RecordLaunchSettings(role.Model, role.ReasoningEffort);
        step.Start(clock.UtcNow, role.Timeout);
        stepRuns.Add(step);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, clock.UtcNow));
        return step;
    }

    private static AgentRunRequest BuildRequest(IntegrationContext context, StepRun step, string prompt)
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

    private async Task<Verdict> JudgeAsync(IntegrationContext context, AgentRunResult run, CommitSha integrationTip, CancellationToken cancellationToken)
    {
        switch (run)
        {
            case { Report: ConflictResolutionReport { Status: ConflictResolutionStatus.Resolved, HeadCommitSha: { } head } report }:
                ReportVerification verification = await _verifier.VerifyReportAsync(
                    context.Location, context.WorktreePath, context.Ticket.BranchName, head, integrationTip, cancellationToken);
                return verification.Outcome == ImplementationOutcome.Implemented
                    ? new Verdict(StepStatus.Succeeded, ConflictResolution.Resolved, Serialize(report), head)
                    : new Verdict(StepStatus.Failed, ConflictResolution.Failed($"Conflict resolution rejected: {verification.Reason}"), Serialize(report));
            case { Report: ConflictResolutionReport report }:
                return new Verdict(StepStatus.Failed, ConflictResolution.Failed($"Conflict resolver reported blocked: {report.Summary}"), Serialize(report));
            case { Report: { } other }:
                return new Verdict(StepStatus.Failed, ConflictResolution.Failed($"Conflict resolver returned an unexpected {other.GetType().Name}."), null);
            case { Outcome: AgentRunOutcome.Cancelled }:
                return new Verdict(StepStatus.Cancelled, new ConflictResolution(ConflictResolutionOutcome.Cancelled, run.FailureReason), null);
            case { Outcome: AgentRunOutcome.TimedOut }:
                return new Verdict(StepStatus.TimedOut, ConflictResolution.Failed($"Conflict resolver timed out: {run.FailureReason}"), null);
            default:
                return new Verdict(StepStatus.Failed, ConflictResolution.Failed($"Conflict resolver failed: {run.FailureReason}"), null);
        }
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    private static string Serialize(ConflictResolutionReport report) => JsonSerializer.Serialize(report);

    private static string Hash(string prompt) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));

    /// <param name="ResolvedHead">The verified ticket branch head after a successful resolution.</param>
    private sealed record Verdict(StepStatus StepStatus, ConflictResolution Resolution, string? ResultJson, CommitSha? ResolvedHead = null);
}
