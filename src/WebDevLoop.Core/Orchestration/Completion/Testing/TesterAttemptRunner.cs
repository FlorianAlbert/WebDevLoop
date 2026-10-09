using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// One tester attempt (workflow step 11): reserves an isolated port, claims the tester step together with a
/// <see cref="TestLease"/>, checks out the integration tip into the run's test workspace, and runs a fresh tester session
/// that starts the application from the configured run instructions on that port. While the tester runs, the app watches the port: an application that does not accept
/// connections within <see cref="TestingOptions.AppStartupTimeout"/>, or whose start fails, aborts the tester and fails the
/// step. Whatever ends the turn — report, failure, timeout, exception, or abort — leftover processes are killed and the
/// lease is released before the step finishes, so a step is never left running.
/// </summary>
/// <remarks>
/// Step ids are derived from the spec and the attempt number, and a spec has at most one active lease, so two runners
/// testing the same spec cannot both claim an attempt. The checkout is only prepared after the claim and removed before the
/// step finishes, so a runner that lost the claim never resets or deletes the checkout the winner's tester is using.
/// </remarks>
public sealed class TesterAttemptRunner(
    IStepRunRepository stepRuns,
    ITestLeaseRepository leases,
    IAgentRunner agents,
    ITestTargetRunner targets,
    IGitWorkspace git,
    PromptRenderer prompts,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    TestingOptions options)
{
    internal async Task<TesterAttempt> RunAsync(TesterContext context, CancellationToken cancellationToken)
    {
        TestPortRange range = context.Settings.TestPortRange;
        if (await targets.ReserveAsync(context.Spec.Id, range, cancellationToken) is not { } target)
        {
            return new TesterAttempt(
                TesterAttemptOutcome.NoFreePort,
                Failure: string.Create(CultureInfo.InvariantCulture, $"No port in the configured test port range {range.Start}-{range.End} is free."));
        }

        Claim claim;
        try
        {
            claim = await ClaimAsync(context, target, cancellationToken);
        }
        catch
        {
            await StopAsync(target);
            throw;
        }

        if (claim.Failure is not null)
        {
            await StopAsync(target);
            return claim.Failure;
        }

        (StepRun step, TestLease lease, string prompt, RoleSettings role) = (claim.Step!, claim.Lease!, claim.Prompt!, claim.Role);
        var request = new AgentRunRequest(
            step.Id,
            new AgentSessionId(step.CopilotSessionId!),
            context.Repository.Ref,
            new AgentModelSettings(role.Model, role.ReasoningEffort, role.Timeout),
            prompt,
            TesterPolicy.For(context.Workspace, target));
        Verdict verdict = await RunInCheckoutAsync(context, request, target, cancellationToken);

        lease.Release(clock.UtcNow);
        Finish(step, verdict, context);
        if (!await SaveAsync(CancellationToken.None))
        {
            return new TesterAttempt(TesterAttemptOutcome.ConcurrencyConflict, step.Id);
        }

        return verdict switch
        {
            { Report: { } report } => new TesterAttempt(TesterAttemptOutcome.Reported, step.Id, report),
            { Status: StepStatus.Cancelled } => new TesterAttempt(TesterAttemptOutcome.Cancelled, step.Id, Failure: verdict.Failure),
            _ => new TesterAttempt(TesterAttemptOutcome.Failed, step.Id, Failure: verdict.Failure),
        };
    }

    /// <summary>Renders the prompt and saves the started tester step together with the lease of the reserved port.</summary>
    private async Task<Claim> ClaimAsync(TesterContext context, TestTarget target, CancellationToken cancellationToken)
    {
        RoleSettings role = context.Settings.For(AgentRole.Tester);
        int attempt = (await stepRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken)).Count(step => step.Kind == StepKind.Test) + 1;
        string prompt;
        try
        {
            prompt = prompts.Render(AgentRole.Tester, role.PromptTemplate, TesterPromptValues.Build(context, attempt, target, options.SkillsRoot));
        }
        catch (Exception exception) when (exception is SettingsValidationException or PromptRenderingException)
        {
            return new Claim(role, Failure: new TesterAttempt(TesterAttemptOutcome.PromptInvalid, Failure: $"The tester prompt cannot be rendered: {exception.Message}"));
        }

        StepRun step = CreateStep(context, attempt, prompt, role);
        TestLease lease = TestLease.Acquire(context.Spec.Id, target.Port, context.Workspace.CheckoutDirectory, clock.UtcNow, role.Timeout + options.LeaseGracePeriod);
        leases.Add(lease);
        Start(step, role.Timeout);
        return await SaveAsync(cancellationToken)
            ? new Claim(role, step, lease, prompt)
            : new Claim(role, Failure: new TesterAttempt(TesterAttemptOutcome.ConcurrencyConflict));
    }

    /// <summary>
    /// Runs the started step in a fresh checkout of the integration tip, which is removed again before the step finishes
    /// (even when the run is cancelled); never throws.
    /// </summary>
    private async Task<Verdict> RunInCheckoutAsync(TesterContext context, AgentRunRequest request, TestTarget target, CancellationToken cancellationToken)
    {
        GitRepositoryLocation location = GitRepositoryLocation.From(context.Repository);
        TestWorkspace workspace = context.Workspace;
        Verdict verdict;
        if (await PrepareCheckoutAsync(location, context, cancellationToken) is { } notPrepared)
        {
            await StopAsync(target);
            verdict = notPrepared;
        }
        else
        {
            verdict = await RunStartedStepAsync(request, target, cancellationToken);
        }

        string? cleanupFailure = await RemoveCheckoutAsync(location, workspace);
        return cleanupFailure is null || verdict.Status == StepStatus.Succeeded
            ? verdict
            : verdict with { Failure = $"{verdict.Failure} {cleanupFailure}".Trim() };
    }

    /// <returns>Null when the checkout is ready; otherwise the verdict of the step that cannot run.</returns>
    private async Task<Verdict?> PrepareCheckoutAsync(GitRepositoryLocation location, TesterContext context, CancellationToken cancellationToken)
    {
        TestWorkspace workspace = context.Workspace;
        try
        {
            await git.PrepareWorktreeAsync(location, new WorktreeSpec(workspace.Branch, context.Head, workspace.CheckoutDirectory), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Verdict(StepStatus.Cancelled, null, "The tester step was cancelled.");
        }
        catch (Exception exception)
        {
            return new Verdict(StepStatus.Failed, null, $"Preparing the test checkout {workspace.CheckoutDirectory} failed: {exception.Message}");
        }
    }

    /// <returns>Why removing the checkout failed; null when it is gone.</returns>
    private async Task<string?> RemoveCheckoutAsync(GitRepositoryLocation location, TestWorkspace workspace)
    {
        try
        {
            await git.CleanupWorktreeAsync(location, workspace.CheckoutDirectory, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return $"Removing the test checkout {workspace.CheckoutDirectory} failed: {exception.Message}";
        }
    }

    /// <summary>Runs the started step to a verdict; never throws, and always kills leftover processes afterwards.</summary>
    private async Task<Verdict> RunStartedStepAsync(AgentRunRequest request, TestTarget target, CancellationToken cancellationToken)
    {
        Verdict verdict;
        try
        {
            verdict = await SuperviseAsync(request, target, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            verdict = new Verdict(StepStatus.Cancelled, null, "The tester step was cancelled.");
        }
        catch (Exception exception)
        {
            verdict = new Verdict(StepStatus.Failed, null, $"The tester step failed: {exception.Message}");
        }

        string? stopFailure = await StopAsync(target);
        return stopFailure is null || verdict.Status == StepStatus.Succeeded
            ? verdict
            : verdict with { Failure = $"{verdict.Failure} {stopFailure}".Trim() };
    }

    /// <summary>Runs the tester turn while watching the reserved port for the application the tester starts.</summary>
    private async Task<Verdict> SuperviseAsync(AgentRunRequest request, TestTarget target, CancellationToken cancellationToken)
    {
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<AgentRunResult> tester = StartTesterAsync(request, turn.Token);
        Task<TestTargetReadiness> readiness = WatchStartupAsync(target, turn.Token);
        try
        {
            string? startupFailure = null;
            if (await Task.WhenAny(tester, readiness) == readiness && StartupFailure(readiness, target) is { } failure)
            {
                startupFailure = failure;
                await agents.AbortAsync(request.SessionId, CancellationToken.None);
                await turn.CancelAsync();
            }

            AgentRunResult result = await tester;
            cancellationToken.ThrowIfCancellationRequested();
            bool appAccepted = readiness.IsCompletedSuccessfully && readiness.Result == TestTargetReadiness.Ready;
            return startupFailure is null ? Judge(result, appAccepted, target) : new Verdict(StepStatus.Failed, null, startupFailure);
        }
        finally
        {
            await turn.CancelAsync();
            await ObserveAsync(tester);
            await ObserveAsync(readiness);
        }
    }

    private async Task<AgentRunResult> StartTesterAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        await agents.StartAsync(request, cancellationToken);

    private async Task<TestTargetReadiness> WatchStartupAsync(TestTarget target, CancellationToken cancellationToken) =>
        await targets.WaitForReadyAsync(target, options.AppStartupTimeout, cancellationToken);

    /// <returns>Why the application start failed; null while it is ready or the wait was merely cancelled.</returns>
    private string? StartupFailure(Task<TestTargetReadiness> readiness, TestTarget target)
    {
        if (readiness.IsCompletedSuccessfully)
        {
            return readiness.Result == TestTargetReadiness.Ready
                ? null
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"The application did not accept connections on the reserved port {target.Port} within {options.AppStartupTimeout}.");
        }

        return readiness.Exception?.GetBaseException() is { } exception
            ? string.Create(CultureInfo.InvariantCulture, $"The application on the reserved port {target.Port} failed to start: {exception.Message}")
            : null;
    }

    private static Verdict Judge(AgentRunResult result, bool appAccepted, TestTarget target) => result switch
    {
        { Report: TestReport { Verdict: TestVerdict.Pass } } when !appAccepted => new Verdict(
            StepStatus.Failed,
            null,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The tester reported a pass, but the application never accepted connections on the reserved port {target.Port}.")),
        { Report: TestReport report } => new Verdict(StepStatus.Succeeded, report, null),
        { Report: { } other } => new Verdict(StepStatus.Failed, null, $"The tester returned an unexpected {other.GetType().Name}."),
        { Outcome: AgentRunOutcome.Cancelled } => new Verdict(StepStatus.Cancelled, null, result.FailureReason),
        { Outcome: AgentRunOutcome.TimedOut } => new Verdict(StepStatus.TimedOut, null, result.FailureReason),
        _ => new Verdict(StepStatus.Failed, null, result.FailureReason),
    };

    /// <returns>Why stopping failed; null when every leftover process is gone.</returns>
    private async Task<string?> StopAsync(TestTarget target)
    {
        try
        {
            // Leftover processes are killed even when the run is cancelled.
            await targets.StopAsync(target, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Stopping leftover processes on port {target.Port} failed: {exception.Message}");
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // Already turned into the verdict, or cancelled because the turn ended.
        }
    }

    private StepRun CreateStep(TesterContext context, int attempt, string prompt, RoleSettings role)
    {
        var id = new StepRunId(string.Create(CultureInfo.InvariantCulture, $"{context.Spec.Id}-test-{attempt}"));
        StepRun step = StepRun.Create(id, context.Spec.Id, null, StepKind.Test, AgentRole.Tester, attempt, Hash(prompt));
        step.CopilotSessionId = ids.NewAgentSessionId(step.Id).Value;
        step.WorktreePath = context.Workspace.CheckoutDirectory;
        step.BranchName = context.Workspace.Branch;
        step.RecordLaunchSettings(role.Model, role.ReasoningEffort);
        return step;
    }

    private void Start(StepRun step, TimeSpan timeout)
    {
        step.Start(clock.UtcNow, timeout);
        stepRuns.Add(step);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, null, step.Id, step.Status, clock.UtcNow));
    }

    private void Finish(StepRun step, Verdict verdict, TesterContext context)
    {
        DateTimeOffset now = clock.UtcNow;
        string? resultJson = verdict.Report is { } report ? TestStepRecord.From(context.Spec.TestCycle, context.Head, report).ToJson() : null;
        step.Finish(verdict.Status, now, resultJson, verdict.Failure);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, null, step.Id, step.Status, now));
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    private static string Hash(string prompt) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));

    /// <param name="Failure">Set when nothing was started; the step, lease, and prompt are set otherwise.</param>
    private sealed record Claim(RoleSettings Role, StepRun? Step = null, TestLease? Lease = null, string? Prompt = null, TesterAttempt? Failure = null);

    /// <param name="Report">The accepted report; null when the step did not succeed.</param>
    private sealed record Verdict(StepStatus Status, TestReport? Report, string? Failure);
}
