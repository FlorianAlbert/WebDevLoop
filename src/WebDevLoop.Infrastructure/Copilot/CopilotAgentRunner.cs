using System.Collections.Concurrent;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot.Reports;
using WebDevLoop.Infrastructure.Copilot.Runtime;
using WebDevLoop.Infrastructure.Skills;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>
/// Runs agent turns on pooled Copilot runtimes. Each session gets the role's tool allow-list, permission handler, report
/// tool, bundled skills, and a credential-scrubbed shell environment; an authentication failure is retried once on a
/// replaced runtime, resuming the same persisted session.
/// </summary>
internal sealed class CopilotAgentRunner(
    CopilotRuntimePool pool,
    BundledSkillsCatalog skills,
    IAgentLogSink logSink,
    IClock clock,
    CopilotRuntimeOptions options) : IAgentRunner
{
    private readonly ConcurrentDictionary<AgentSessionId, ICopilotAgentSession> _activeSessions = new();

    public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        RunAsync(request, sessionExists: false, cancellationToken);

    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        RunAsync(request, sessionExists: true, cancellationToken);

    public async Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken)
    {
        if (_activeSessions.TryGetValue(sessionId, out ICopilotAgentSession? session))
        {
            await session.AbortAsync(cancellationToken);
        }
    }

    private async Task<AgentRunResult> RunAsync(AgentRunRequest request, bool sessionExists, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var log = new AgentLogForwarder(request.StepRunId, logSink, clock);

        BundledSkills bundledSkills;
        try
        {
            bundledSkills = skills.Resolve();
        }
        catch (SkillManifestException exception)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.Failed, exception.Message);
        }

        var session = new SessionSetup(
            request,
            new AgentSessionPolicy(request.Policy, bundledSkills.Root),
            AgentReportToolFactory.For(request.Role),
            bundledSkills.Root,
            request.Policy.BuildEnvironment(options.InheritedEnvironment()),
            log);

        try
        {
            Attempt attempt = await RunAttemptAsync(session, sessionExists, cancellationToken);
            if (attempt is Attempt.AuthenticationRejected rejected)
            {
                await pool.ReplaceAsync(rejected.Runtime, cancellationToken);
                attempt = await RunAttemptAsync(session, sessionExists || rejected.SessionOpened, cancellationToken);
            }

            return attempt switch
            {
                Attempt.Finished finished => finished.Result,
                Attempt.AuthenticationRejected second => AgentRunResult.NotReported(AgentRunOutcome.AuthenticationFailed, second.Reason),
                _ => throw new InvalidOperationException($"Unexpected attempt result {attempt}."),
            };
        }
        catch (CopilotAuthenticationException exception)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.AuthenticationFailed, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.Cancelled, "The agent step was cancelled.");
        }
    }

    private async Task<Attempt> RunAttemptAsync(SessionSetup setup, bool sessionExists, CancellationToken cancellationToken)
    {
        AgentRunRequest request = setup.Request;
        // Unavailable credentials (CopilotAuthenticationException here) cannot be fixed by replacing a runtime.
        CopilotRuntimeHandle handle = await pool.AcquireAsync(request.Repository, setup.ShellEnvironment, cancellationToken);
        await using (handle)
        {
            var turn = new AgentTurn(setup.ReportTool, setup.Log);
            CopilotSessionSpec spec = BuildSpec(setup, turn, handle.Token);
            ICopilotAgentSession? session = null;
            try
            {
                session = sessionExists
                    ? await handle.Runtime.ResumeSessionAsync(spec, cancellationToken)
                    : await handle.Runtime.CreateSessionAsync(spec, cancellationToken);
                _activeSessions[request.SessionId] = session;
                await session.SendAsync(request.Prompt, cancellationToken);
                TurnEnd end = await turn.Completion.WaitAsync(request.Settings.Timeout, cancellationToken);
                return end switch
                {
                    TurnEnd.Reported reported => new Attempt.Finished(AgentRunResult.Reported(reported.Report)),
                    TurnEnd.Rejected invalid => Finish(AgentRunOutcome.InvalidReport, invalid.Reason),
                    TurnEnd.Missing => Finish(AgentRunOutcome.MissingReport, $"The agent ended its turn without calling {setup.ReportTool.Name}."),
                    TurnEnd.Errored { IsAuthenticationFailure: true } error => new Attempt.AuthenticationRejected(handle.Lease.Key, error.Message, SessionOpened: true),
                    TurnEnd.Errored error => Finish(AgentRunOutcome.Failed, error.Message),
                    _ => throw new InvalidOperationException($"Unexpected turn end {end}."),
                };
            }
            catch (CopilotAuthenticationException exception)
            {
                return new Attempt.AuthenticationRejected(handle.Lease.Key, exception.Message, SessionOpened: session is not null);
            }
            catch (CopilotSessionNotFoundException exception)
            {
                setup.Log.Append(AgentLogKind.Error, exception.Message);
                return Finish(AgentRunOutcome.SessionNotFound, exception.Message);
            }
            catch (TimeoutException)
            {
                await AbortTurnAsync(session, setup.Log);
                return Finish(AgentRunOutcome.TimedOut, $"The agent did not finish within {request.Settings.Timeout}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await AbortTurnAsync(session, setup.Log);
                return Finish(AgentRunOutcome.Cancelled, "The agent step was cancelled.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                setup.Log.Append(AgentLogKind.Error, exception.Message);
                return Finish(AgentRunOutcome.Failed, exception.Message);
            }
            finally
            {
                if (session is not null)
                {
                    _activeSessions.TryRemove(new KeyValuePair<AgentSessionId, ICopilotAgentSession>(request.SessionId, session));
                    await session.DisposeAsync();
                }
            }
        }
    }

    private CopilotSessionSpec BuildSpec(SessionSetup setup, AgentTurn turn, GitHubAccessToken copilotToken) => new(
        setup.Request.SessionId,
        setup.Request.Policy.Paths.WorkingDirectory,
        setup.Request.Settings,
        [setup.SkillsRoot],
        setup.Policy.Tools,
        new CopilotReportTool(setup.ReportTool.Name, setup.ReportTool.Description, setup.ReportTool.ParametersSchema, turn.OnReport),
        permission => Authorize(setup, permission),
        SessionAuth(setup.Request, copilotToken),
        turn.OnEvent);

    private static AgentPermissionDecision Authorize(SessionSetup setup, AgentPermissionRequest permission)
    {
        AgentPermissionDecision decision = setup.Policy.Authorize(permission);
        if (!decision.IsApproved)
        {
            setup.Log.Append(AgentLogKind.PermissionDenied, $"{permission.RawKind} '{permission.Target}': {decision.Reason}");
        }

        return decision;
    }

    /// <summary>App installation tokens live in the runtime environment; user/PAT tokens go to the session, via callback when they rotate.</summary>
    private CopilotSessionAuth SessionAuth(AgentRunRequest request, GitHubAccessToken copilotToken) => copilotToken switch
    {
        { Kind: GitHubTokenKind.AppInstallation } => CopilotSessionAuth.None,
        { ExpiresAt: null } => CopilotSessionAuth.StaticToken(copilotToken.Value),
        _ => CopilotSessionAuth.RotatingToken(async cancellationToken =>
        {
            GitHubAccessToken current = await pool.RequestTokenAsync(request.Repository, cancellationToken);
            TimeSpan remaining = current.ExpiresAt is { } expiresAt ? expiresAt - clock.UtcNow : TimeSpan.Zero;
            return new CopilotUserToken(current.Value, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }),
    };

    private static async Task AbortTurnAsync(ICopilotAgentSession? session, AgentLogForwarder log)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await session.AbortAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            log.Append(AgentLogKind.Error, $"Aborting the session failed: {exception.Message}");
        }
    }

    private static Attempt.Finished Finish(AgentRunOutcome outcome, string reason) => new(AgentRunResult.NotReported(outcome, reason));

    private sealed record SessionSetup(
        AgentRunRequest Request,
        AgentSessionPolicy Policy,
        AgentReportTool ReportTool,
        string SkillsRoot,
        IReadOnlyDictionary<string, string> ShellEnvironment,
        AgentLogForwarder Log);

    private abstract record Attempt
    {
        public sealed record Finished(AgentRunResult Result) : Attempt;

        public sealed record AuthenticationRejected(CopilotRuntimeKey Runtime, string Reason, bool SessionOpened) : Attempt;
    }
}
