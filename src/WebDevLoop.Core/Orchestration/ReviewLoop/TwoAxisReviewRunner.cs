using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Runs one review round (workflow steps 5 and 9): each requested axis gets its own reviewer step and Copilot session with
/// the read-only reviewer policy, and the reviewers run concurrently without seeing each other's results. Axes whose turn
/// fails are retried in a fresh session up to <c>MaxRetries</c>. Reusable for ticket branches and for the final
/// parent-spec review of the integration branch (<see cref="ReviewScope"/>).
/// </summary>
/// <remarks>
/// Review step ids are derived from the reviewed owner, kind, axis, and per-axis attempt, so two runners reviewing the same
/// round add the same ids and only one of them can save its claim; a runner that finds a reviewer step of the owner still
/// active does not claim at all. A checkout the runner creates (<see cref="ReviewRequest.CreatesCheckout"/>) only exists
/// while its claimed steps are active, so a runner that lost the claim never touches the winner's checkout.
/// </remarks>
public sealed class TwoAxisReviewRunner(
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
    ReviewOptions options)
{
    private readonly ReviewLoopJournal _journal = new(stepRuns, outbox, clock);

    /// <exception cref="ArgumentException">A ticket-scope request without a ticket, or a parent-spec request with one.</exception>
    public async Task<ReviewRoundResult> RunAsync(ReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Scope == ReviewScope.Ticket) != request.TicketRunId.HasValue)
        {
            throw new ArgumentException("Ticket reviews need a ticket; parent-spec reviews must not have one.", nameof(request));
        }

        if (await LoadContextAsync(request, cancellationToken) is not { } context)
        {
            return ReviewRoundResult.Failed(AttentionReasons.InternalInconsistency(
                $"Spec run '{request.SpecRunId}', its repository, its ticket, or its integration tip is missing.", request.Scope == ReviewScope.Ticket));
        }

        var reports = new Dictionary<FindingAxis, ReviewReport>();
        var failures = new Dictionary<FindingAxis, string>();
        FindingAxis[] pending = request.Axes.Distinct().Order().ToArray();
        int attempts = context.Settings.MaxRetries + 1;
        for (int round = 0; round < attempts && pending.Length > 0; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await StartTurnsAsync(context, pending, cancellationToken) is not { } turns)
            {
                return ReviewRoundResult.ConcurrencyConflict;
            }

            if (turns.Failure is { } renderFailure)
            {
                return ReviewRoundResult.Failed(AttentionReasons.PromptNotRenderable("Reviewer", renderFailure, request.Scope == ReviewScope.Ticket));
            }

            AgentRunResult[] results = await RunTurnsAsync(context, turns.Started, cancellationToken);
            bool cancelled = false;
            foreach ((ReviewerTurn turn, AgentRunResult result) in turns.Started.Zip(results))
            {
                AxisVerdict verdict = Judge(turn.Axis, result);
                string? resultJson = verdict.Report is { } report ? ReviewStepRecord.From(request.Round, request.Target.DiffHead, report).ToJson() : null;
                _journal.Finish(turn.Step, verdict.StepStatus, resultJson, verdict.Failure);
                cancelled |= verdict.StepStatus == StepStatus.Cancelled;
                if (verdict.Report is { } accepted)
                {
                    reports[turn.Axis] = accepted;
                }
                else
                {
                    failures[turn.Axis] = verdict.Failure!;
                }
            }

            if (!await SaveAsync(cancellationToken))
            {
                return ReviewRoundResult.ConcurrencyConflict;
            }

            if (cancelled)
            {
                return new ReviewRoundResult(ReviewRoundOutcome.Cancelled, [], "A reviewer turn was cancelled.");
            }

            pending = pending.Where(axis => !reports.ContainsKey(axis)).ToArray();
        }

        return pending.Length == 0
            ? new ReviewRoundResult(ReviewRoundOutcome.Completed, reports.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray())
            : ReviewRoundResult.Failed(ReviewFailure(
                request.Scope,
                string.Join(" ", pending.Select(axis => $"The {ReviewJson.Name(axis)} review failed after {attempts} attempt(s): {failures[axis]}"))));
    }

    private static AttentionReason ReviewFailure(ReviewScope scope, string details) =>
        scope == ReviewScope.Ticket ? AttentionReasons.ReviewFailed(details) : AttentionReasons.ParentReviewFailed(details);

    private async Task<ReviewContext?> LoadContextAsync(ReviewRequest request, CancellationToken cancellationToken)
    {
        SpecRun? spec = await specRuns.GetAsync(request.SpecRunId, cancellationToken);
        RepositoryRecord? repository = spec is null ? null : await repositories.GetAsync(spec.RepositoryId, cancellationToken);
        TicketRun? ticket = request.TicketRunId is { } ticketId ? await ticketRuns.GetAsync(ticketId, cancellationToken) : null;
        if (spec is null || repository is null || (request.TicketRunId.HasValue && ticket is null))
        {
            return null;
        }

        GitRepositoryLocation location = GitRepositoryLocation.From(repository);
        CommitSha? integrationTip = await git.GetBranchTipAsync(location, spec.IntegrationBranch, GitRefScope.Local, cancellationToken)
            ?? spec.IntegrationTipSha;
        if (integrationTip is not { } tip)
        {
            return null;
        }

        return new ReviewContext(request, spec, ticket, repository, await settings.GetAsync(spec.RepositoryId, cancellationToken))
        {
            IntegrationTip = tip,
            ChangedFiles = await git.GetChangedFilesAsync(location, request.Target.DiffBase, request.Target.DiffHead, cancellationToken),
        };
    }

    /// <returns>Null when another runner claimed the same turns first.</returns>
    private async Task<StartedTurns?> StartTurnsAsync(ReviewContext context, FindingAxis[] axes, CancellationToken cancellationToken)
    {
        IReadOnlyList<StepRun> existing = await ExistingStepsAsync(context, cancellationToken);
        if (existing.Any(step => step.IsActive))
        {
            return null;
        }

        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken);
        IReadOnlyList<TicketDependency> dependencies = await ticketRuns.ListDependenciesAsync(context.Spec.Id, cancellationToken);
        var turns = new List<(FindingAxis Axis, AgentRole Role, int Attempt, string Prompt)>();
        foreach (FindingAxis axis in axes)
        {
            AgentRole role = ReviewAxes.ReviewerFor(axis);
            int attempt = existing.Count(step => step.AgentRole == role) + 1;
            try
            {
                IReadOnlyDictionary<string, string> values = ReviewerPromptValues.Build(context, axis, attempt, options.SkillsRoot, tickets, dependencies);
                turns.Add((axis, role, attempt, prompts.Render(role, context.Settings.For(role).PromptTemplate, values)));
            }
            catch (Exception exception) when (exception is SettingsValidationException or PromptRenderingException)
            {
                return new StartedTurns([], $"The {ReviewJson.Name(axis)} reviewer prompt cannot be rendered: {exception.Message}");
            }
        }

        ReviewerTurn[] started = turns.Select(turn => StartTurn(context, turn.Axis, turn.Role, turn.Attempt, turn.Prompt)).ToArray();
        return await SaveAsync(cancellationToken) ? new StartedTurns(started, null) : null;
    }

    private async Task<IReadOnlyList<StepRun>> ExistingStepsAsync(ReviewContext context, CancellationToken cancellationToken)
    {
        StepKind kind = KindOf(context.Request.Scope);
        IReadOnlyList<StepRun> steps = context.Ticket is { } ticket
            ? await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken)
            : (await stepRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken)).Where(step => step.TicketRunId is null).ToArray();
        return steps.Where(step => step.Kind == kind).ToArray();
    }

    private ReviewerTurn StartTurn(ReviewContext context, FindingAxis axis, AgentRole role, int attempt, string prompt)
    {
        ReviewTarget target = context.Request.Target;
        StepKind kind = KindOf(context.Request.Scope);
        StepRun step = StepRun.Create(StepIdFor(context, kind, axis, attempt), context.Spec.Id, context.Ticket?.Id, kind, role, attempt, Hash(prompt));
        step.CopilotSessionId = ids.NewAgentSessionId(step.Id).Value;
        step.WorktreePath = target.WorkingDirectory;
        step.BranchName = target.Branch;
        RoleSettings roleSettings = context.Settings.For(role);
        step.RecordLaunchSettings(roleSettings.Model, roleSettings.ReasoningEffort);
        _journal.Start(step, roleSettings.Timeout);
        var request = new AgentRunRequest(
            step.Id,
            new AgentSessionId(step.CopilotSessionId),
            context.Repository.Ref,
            new AgentModelSettings(roleSettings.Model, roleSettings.ReasoningEffort, roleSettings.Timeout),
            prompt,
            RoleCapabilityPolicies.For(role, new AgentWorkspace(target.WorkingDirectory, context.NotesDirectory)));
        return new ReviewerTurn(axis, step, request);
    }

    /// <summary>Deterministic per owner/kind/axis/attempt so duplicate runners collide on the step's primary key.</summary>
    private static StepRunId StepIdFor(ReviewContext context, StepKind kind, FindingAxis axis, int attempt)
    {
        string owner = context.Ticket?.Id.Value ?? context.Spec.Id.Value;
        string axisName = ReviewJson.Name(axis).Replace('_', '-');
        string kindName = kind == StepKind.ParentReview ? "parent-review" : "review";
        return new StepRunId(string.Create(CultureInfo.InvariantCulture, $"{owner}-{kindName}-{axisName}-{attempt}"));
    }

    private static StepKind KindOf(ReviewScope scope) => scope == ReviewScope.ParentSpec ? StepKind.ParentReview : StepKind.Review;

    /// <summary>Runs the claimed turns concurrently, inside a fresh checkout when the request creates one.</summary>
    private async Task<AgentRunResult[]> RunTurnsAsync(ReviewContext context, IReadOnlyList<ReviewerTurn> turns, CancellationToken cancellationToken)
    {
        if (!context.Request.CreatesCheckout)
        {
            return await Task.WhenAll(turns.Select(turn => RunTurnAsync(turn, cancellationToken)));
        }

        ReviewTarget target = context.Request.Target;
        GitRepositoryLocation location = GitRepositoryLocation.From(context.Repository);
        AgentRunResult[] results = await PrepareCheckoutAsync(location, target, cancellationToken) is { } notPrepared
            ? turns.Select(_ => notPrepared).ToArray()
            : await Task.WhenAll(turns.Select(turn => RunTurnAsync(turn, cancellationToken)));
        return await RemoveCheckoutAsync(location, target) is { } cleanupFailure
            ? results.Select(result => result.Report is null ? AgentRunResult.NotReported(result.Outcome, $"{result.FailureReason} {cleanupFailure}") : result).ToArray()
            : results;
    }

    /// <returns>Null when the checkout is ready; otherwise the result of every turn that cannot run.</returns>
    private async Task<AgentRunResult?> PrepareCheckoutAsync(GitRepositoryLocation location, ReviewTarget target, CancellationToken cancellationToken)
    {
        try
        {
            await git.PrepareWorktreeAsync(location, new WorktreeSpec(target.Branch, target.DiffHead, target.WorkingDirectory), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.Cancelled, "The review was cancelled while preparing its checkout.");
        }
        catch (Exception exception)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.Failed, $"Preparing the review checkout {target.WorkingDirectory} failed: {exception.Message}");
        }
    }

    /// <summary>Removed even when the run is cancelled; the next round checks out the reviewed commit again.</summary>
    /// <returns>Why removing the checkout failed; null when it is gone.</returns>
    private async Task<string?> RemoveCheckoutAsync(GitRepositoryLocation location, ReviewTarget target)
    {
        try
        {
            await git.CleanupWorktreeAsync(location, target.WorkingDirectory, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return $"Removing the review checkout {target.WorkingDirectory} failed: {exception.Message}";
        }
    }

    /// <summary>An unexpected error fails only this turn (retried like any failed turn), so no step stays running.</summary>
    private async Task<AgentRunResult> RunTurnAsync(ReviewerTurn turn, CancellationToken cancellationToken)
    {
        try
        {
            return await agents.StartAsync(turn.Request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.Failed, $"The reviewer turn failed unexpectedly: {exception.Message}");
        }
    }

    private static AxisVerdict Judge(FindingAxis axis, AgentRunResult result) => result switch
    {
        { Report: ReviewReport report } when report.Axis == axis => new AxisVerdict(StepStatus.Succeeded, report, null),
        { Report: ReviewReport report } => new AxisVerdict(StepStatus.Failed, null, $"The {ReviewJson.Name(axis)} reviewer reported the {ReviewJson.Name(report.Axis)} axis."),
        { Report: { } other } => new AxisVerdict(StepStatus.Failed, null, $"The reviewer returned an unexpected {other.GetType().Name}."),
        { Outcome: AgentRunOutcome.Cancelled } => new AxisVerdict(StepStatus.Cancelled, null, result.FailureReason),
        { Outcome: AgentRunOutcome.TimedOut } => new AxisVerdict(StepStatus.TimedOut, null, result.FailureReason),
        _ => new AxisVerdict(StepStatus.Failed, null, result.FailureReason),
    };

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    private static string Hash(string prompt) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));

    private sealed record ReviewerTurn(FindingAxis Axis, StepRun Step, AgentRunRequest Request);

    /// <param name="Failure">Set when the prompts could not be rendered; nothing was started then.</param>
    private sealed record StartedTurns(IReadOnlyList<ReviewerTurn> Started, string? Failure);

    /// <param name="Report">The accepted report; null when the turn failed.</param>
    private sealed record AxisVerdict(StepStatus StepStatus, ReviewReport? Report, string? Failure);
}
