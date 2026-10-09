using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>
/// Workflow step 2 (optional): runs the explorer agent in a clean, throw-away checkout of the integration tip with an
/// app-allocated notes directory outside every repository checkout. Each attempt is a persisted <see cref="StepRun"/>;
/// a blocked or failed attempt is retried up to <c>MaxRetries</c> times. Already succeeded exploration is not repeated.
/// </summary>
public sealed class SpecExplorer(
    IStepRunRepository stepRuns,
    ITicketRunRepository ticketRuns,
    IGitWorkspace git,
    IAgentRunner agents,
    IAppDirectoryProvisioner directories,
    PromptRenderer prompts,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    SpecPreparationOptions options)
{
    private const AgentRole Role = AgentRole.Explorer;

    public async Task<ExplorationResult> ExploreAsync(
        SpecRun run,
        RepositoryRecord repository,
        EffectiveSettings settings,
        CancellationToken cancellationToken)
    {
        StepRun[] previousAttempts = (await stepRuns.ListBySpecRunAsync(run.Id, cancellationToken))
            .Where(step => step.Kind == StepKind.Explore)
            .ToArray();
        if (previousAttempts.Any(step => step.Status == StepStatus.Succeeded))
        {
            return ExplorationResult.Explored;
        }

        RunWorkspaceLayout layout = RunWorkspaceLayout.For(settings.WorkspaceRootDirectory, run.Id);
        if (PathConfinement.IsUnder(layout.ExplorationNotesDirectory, repository.LocalPath))
        {
            return ExplorationResult.Failed(
                $"Exploration notes directory '{layout.ExplorationNotesDirectory}' would be inside the repository clone '{repository.LocalPath}'.");
        }

        directories.EnsureExists(layout.ExplorationNotesDirectory);
        var context = new AttemptContext(run, repository, settings, layout, GitRepositoryLocation.From(repository));
        int maxAttempts = settings.MaxRetries + 1;
        string? lastFailure = null;
        for (int attempt = previousAttempts.Length + 1; attempt <= previousAttempts.Length + maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string prompt;
            try
            {
                prompt = await RenderPromptAsync(context, attempt, cancellationToken);
            }
            catch (Exception exception) when (exception is SettingsValidationException or PromptRenderingException)
            {
                return ExplorationResult.Failed($"The explorer prompt cannot be rendered: {exception.Message}");
            }

            AttemptResult? finished = await RunAttemptAsync(context, attempt, prompt, cancellationToken);
            if (finished is null)
            {
                return ExplorationResult.Conflict;
            }

            if (finished.Status == StepStatus.Succeeded)
            {
                return ExplorationResult.Explored;
            }

            lastFailure = finished.Failure;
        }

        return ExplorationResult.Failed($"Exploration failed after {maxAttempts} attempt(s): {lastFailure}");
    }

    /// <returns>The step's final status and failure reason, or null when a save lost a concurrency race.</returns>
    private async Task<AttemptResult?> RunAttemptAsync(
        AttemptContext context,
        int attempt,
        string prompt,
        CancellationToken cancellationToken)
    {
        (SpecRun run, RepositoryRecord repository, EffectiveSettings settings, RunWorkspaceLayout layout, GitRepositoryLocation location) = context;
        RoleSettings role = settings.For(Role);
        StepRun step = StepRun.Create(ids.NewStepRunId(), run.Id, null, StepKind.Explore, Role, attempt, Hash(prompt));
        AgentSessionId sessionId = ids.NewAgentSessionId(step.Id);
        step.CopilotSessionId = sessionId.Value;
        step.WorktreePath = layout.ExplorerCheckoutDirectory;
        step.BranchName = layout.ExplorerBranch;
        step.Start(clock.UtcNow, role.Timeout);
        stepRuns.Add(step);
        if (!await SaveStepAsync(step, cancellationToken))
        {
            return null;
        }

        await git.PrepareWorktreeAsync(
            location,
            new WorktreeSpec(layout.ExplorerBranch, run.IntegrationTipSha!.Value, layout.ExplorerCheckoutDirectory),
            cancellationToken);
        var request = new AgentRunRequest(
            step.Id,
            sessionId,
            repository.Ref,
            new AgentModelSettings(role.Model, role.ReasoningEffort, role.Timeout),
            prompt,
            RoleCapabilityPolicies.For(Role, new AgentWorkspace(layout.ExplorerCheckoutDirectory, layout.ExplorationNotesDirectory)));
        AgentRunResult result = await agents.StartAsync(request, cancellationToken);
        await git.CleanupWorktreeAsync(location, layout.ExplorerCheckoutDirectory, cancellationToken);

        (StepStatus status, string? failure, string? resultJson) = Evaluate(result);
        step.Finish(status, clock.UtcNow, resultJson, failure);
        return await SaveStepAsync(step, cancellationToken) ? new AttemptResult(status, failure) : null;
    }

    private async Task<string> RenderPromptAsync(AttemptContext context, int attempt, CancellationToken cancellationToken)
    {
        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(context.Run.Id, cancellationToken);
        IReadOnlyList<TicketDependency> dependencies = await ticketRuns.ListDependenciesAsync(context.Run.Id, cancellationToken);
        IReadOnlyDictionary<string, string> values = ExplorerPromptValues.Build(
            context.Run, context.Repository, context.Settings, options.SkillsRoot, context.Layout, attempt, tickets, dependencies);
        return prompts.Render(Role, context.Settings.For(Role).PromptTemplate, values);
    }

    private static (StepStatus Status, string? Failure, string? ResultJson) Evaluate(AgentRunResult result) => result switch
    {
        { Report: ExplorationReport { Status: ReportStatus.Completed } report } => (StepStatus.Succeeded, null, Serialize(report)),
        { Report: ExplorationReport report } => (StepStatus.Failed, $"Explorer reported blocked: {report.Summary}", Serialize(report)),
        { Report: { } other } => (StepStatus.Failed, $"Explorer returned an unexpected {other.GetType().Name}.", null),
        { Outcome: AgentRunOutcome.TimedOut } => (StepStatus.TimedOut, result.FailureReason, null),
        { Outcome: AgentRunOutcome.Cancelled } => (StepStatus.Cancelled, result.FailureReason, null),
        _ => (StepStatus.Failed, result.FailureReason, null),
    };

    private async Task<bool> SaveStepAsync(StepRun step, CancellationToken cancellationToken)
    {
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, clock.UtcNow));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
    }

    private static string Serialize(ExplorationReport report) => JsonSerializer.Serialize(report);

    private static string Hash(string prompt) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));

    private sealed record AttemptResult(StepStatus Status, string? Failure);

    private sealed record AttemptContext(
        SpecRun Run,
        RepositoryRecord Repository,
        EffectiveSettings Settings,
        RunWorkspaceLayout Layout,
        GitRepositoryLocation Location);
}
