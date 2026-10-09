using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// Workflow steps 11–12 for a spec in <c>Testing</c> (entry point for <see cref="ITestingLauncher"/>): a tester agent
/// exercises the integrated application, which it starts itself from the repository's run instructions on a port the app
/// reserved (<see cref="TesterAttemptRunner"/>), from a checkout of the integration tip. Failed tester attempts are retried
/// in a fresh session up to <c>MaxRetries</c>.
/// <list type="bullet">
/// <item><c>pass</c>: the spec stays in <c>Testing</c> and <see cref="SpecTestingPassed"/> hands it to completion.</item>
/// <item><c>issues_found</c>: the issues become finding tickets (<see cref="FindingTicketIssuer"/>, same idempotent service
/// as parent-review findings) and the spec returns to <c>Running</c>; once every ticket is done the parent-spec review and
/// testing repeat. Issues in the last allowed cycle (<c>TesterCycleLimit</c>), or issues that only repeat already-done
/// tickets, move the spec to <c>NeedsAttention</c>.</item>
/// <item><c>blocked</c>: the spec needs attention.</item>
/// </list>
/// The verdict is persisted with the tester step, so a restarted runner reuses it instead of testing the same tip again.
/// </summary>
public sealed class SpecTestRunner(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IEffectiveSettingsProvider settings,
    IGitWorkspace git,
    TesterAttemptRunner attempts,
    FindingTicketIssuer findings,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private readonly SpecRunJournal _journal = new(outbox, clock);

    public async Task<TestingResult> RunAsync(TestingAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        SpecRun? spec = await specRuns.GetAsync(assignment.SpecRunId, cancellationToken);
        if (spec is not { Status: SpecRunStatus.Testing })
        {
            return TestingResult.NotTesting;
        }

        IReadOnlyList<StepRun> steps = await stepRuns.ListBySpecRunAsync(spec.Id, cancellationToken);
        if (steps.Any(step => step is { Kind: StepKind.Test, IsActive: true }))
        {
            return TestingResult.AlreadyRunning;
        }

        if (await LoadContextAsync(spec, cancellationToken) is not { } context)
        {
            return await NeedsAttentionAsync(
                spec, TestingOutcome.Failed, $"The repository or integration tip of spec run '{spec.Id}' is missing.", [], cancellationToken);
        }

        if (TestStepRecord.Latest(steps, spec.TestCycle, context.Head) is { } persisted)
        {
            return await ConcludeAsync(context, persisted.StepRunId, persisted.Record.ToReport(), cancellationToken);
        }

        TesterAttempt tested = await TestAsync(context, cancellationToken);
        return tested.Outcome switch
        {
            TesterAttemptOutcome.Reported => await ConcludeAsync(context, tested.StepRunId!.Value, tested.Report!, cancellationToken),
            TesterAttemptOutcome.Cancelled => new TestingResult(TestingOutcome.Cancelled, [], tested.Failure),
            TesterAttemptOutcome.ConcurrencyConflict => TestingResult.ConcurrencyConflict,
            _ => await NeedsAttentionAsync(spec, TestingOutcome.Failed, tested.Failure!, [], cancellationToken),
        };
    }

    private async Task<TesterContext?> LoadContextAsync(SpecRun spec, CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(spec.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return null;
        }

        CommitSha? head = await git.GetBranchTipAsync(GitRepositoryLocation.From(repository), spec.IntegrationBranch, GitRefScope.Local, cancellationToken)
            ?? spec.IntegrationTipSha;
        if (head is not { } tip)
        {
            return null;
        }

        EffectiveSettings effective = await settings.GetAsync(spec.RepositoryId, cancellationToken);
        return new TesterContext(
            spec,
            repository,
            effective,
            tip,
            TestWorkspace.For(effective.WorkspaceRootDirectory, spec.Id),
            await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken),
            await ticketRuns.ListDependenciesAsync(spec.Id, cancellationToken));
    }

    /// <summary>
    /// Runs tester attempts until one reports or the retries are exhausted; each attempt checks out the integration tip only
    /// after claiming its step (see <see cref="TesterAttemptRunner"/>).
    /// </summary>
    private async Task<TesterAttempt> TestAsync(TesterContext context, CancellationToken cancellationToken)
    {
        int allowed = context.Settings.MaxRetries + 1;
        var failures = new List<string>();
        for (int attempt = 1; attempt <= allowed; attempt++)
        {
            TesterAttempt tested = await attempts.RunAsync(context, cancellationToken);
            if (tested.Outcome != TesterAttemptOutcome.Failed)
            {
                return tested;
            }

            failures.Add(tested.Failure ?? "no reason given");
        }

        return new TesterAttempt(
            TesterAttemptOutcome.Failed,
            Failure: string.Create(CultureInfo.InvariantCulture, $"The tester failed after {allowed} attempt(s): {string.Join(" | ", failures)}"));
    }

    private async Task<TestingResult> ConcludeAsync(TesterContext context, StepRunId stepRunId, TestReport report, CancellationToken cancellationToken)
    {
        SpecRun spec = context.Spec;
        switch (report.Verdict)
        {
            case TestVerdict.Pass:
                outbox.Append(new SpecTestingPassed(spec.Id, spec.RepositoryId, spec.TestCycle, clock.UtcNow));
                return await SaveAsync(cancellationToken) ? new TestingResult(TestingOutcome.Passed, []) : TestingResult.ConcurrencyConflict;
            case TestVerdict.Blocked:
                return await NeedsAttentionAsync(
                    spec, TestingOutcome.Blocked, $"The tester could not test the application: {report.Summary}", [], cancellationToken);
            default:
                SourcedFinding[] sourced = report.Issues.Select(issue => new SourcedFinding(StepKind.Test, stepRunId, issue)).ToArray();
                return await IssueFindingsAsync(context, sourced, cancellationToken);
        }
    }

    private async Task<TestingResult> IssueFindingsAsync(TesterContext context, SourcedFinding[] sourced, CancellationToken cancellationToken)
    {
        SpecRun spec = context.Spec;
        FindingIssuanceResult issuance = await findings.IssueAsync(spec, sourced, cancellationToken);
        if (issuance.Outcome != FindingIssuanceOutcome.Issued)
        {
            return TestingResult.ConcurrencyConflict;
        }

        Dictionary<TicketRunId, TicketRun> tickets = (await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken)).ToDictionary(ticket => ticket.Id);
        string issueList = string.Join(", ", issuance.Tickets.Select(ticket => $"#{ticket.Issue.Number}"));
        if (issuance.Tickets.All(ticket => tickets[ticket.TicketRunId].IsTerminal))
        {
            return await NeedsAttentionAsync(
                spec,
                TestingOutcome.NoNewWork,
                $"Test cycle {spec.TestCycle} only repeated issues whose tickets are already done ({issueList}); working the tickets again cannot resolve them.",
                issuance.Tickets,
                cancellationToken);
        }

        int limit = context.Settings.TesterCycleLimit;
        if (spec.TestCycle >= limit)
        {
            return await NeedsAttentionAsync(
                spec,
                TestingOutcome.CycleLimitReached,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The tester still found {sourced.Length} issue(s) after {spec.TestCycle} test cycle(s); the limit is {limit}. Finding tickets: {issueList}."),
                issuance.Tickets,
                cancellationToken);
        }

        _journal.Move(spec, SpecRunStatus.Running);
        return await SaveAsync(cancellationToken)
            ? new TestingResult(TestingOutcome.FindingTicketsCreated, issuance.Tickets)
            : TestingResult.ConcurrencyConflict;
    }

    private async Task<TestingResult> NeedsAttentionAsync(
        SpecRun spec, TestingOutcome outcome, string reason, IReadOnlyList<FindingTicket> tickets, CancellationToken cancellationToken)
    {
        _journal.MarkNeedsAttention(spec, reason);
        return await SaveAsync(cancellationToken) ? new TestingResult(outcome, tickets, reason) : TestingResult.ConcurrencyConflict;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
}
