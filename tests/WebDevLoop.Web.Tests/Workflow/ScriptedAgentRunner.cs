using System.Collections.Concurrent;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>One agent turn as seen by the fake Copilot runner.</summary>
internal sealed record AgentCall(
    AgentRole Role,
    StepKind Kind,
    int? TicketIssue,
    bool Resumed,
    bool HasGitHubWriteToken,
    IReadOnlySet<int> ClosedIssuesAtStart,
    int ConcurrentImplementers);

/// <summary>What the scripted agents do differently from the default happy path.</summary>
internal sealed class AgentScript
{
    /// <summary>Tickets (by issue number) whose first coding-standards review reports a finding.</summary>
    public HashSet<int> CodingStandardsFindingsOnFirstReview { get; } = [];

    /// <summary>Tickets whose implementer never reports (it hangs until the turn is cancelled, e.g. by shutdown).</summary>
    public HashSet<int> HangingImplementers { get; } = [];

    public TaskCompletionSource HangingImplementerStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Tickets whose first implementer turns wait for each other before committing, which only completes when they are
    /// dispatched concurrently (a sequential dispatcher would time out the rendezvous).
    /// </summary>
    public Rendezvous ConcurrentImplementers { get; set; } = Rendezvous.None;
}

internal sealed class Rendezvous(params int[] tickets)
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pending = tickets.Length;

    public static Rendezvous None { get; } = new();

    public async Task ArriveAsync(int ticket, CancellationToken cancellationToken)
    {
        if (!tickets.Contains(ticket) || _allArrived.Task.IsCompleted)
        {
            return;
        }

        if (Interlocked.Decrement(ref _pending) == 0)
        {
            _allArrived.TrySetResult();
        }

        await _allArrived.Task.WaitAsync(MaxWait, cancellationToken);
    }
}

/// <summary>
/// Fake <see cref="IAgentRunner"/> that does each role's job deterministically: the explorer reports, the implementer merges
/// the run's integration branch and commits a file named after its ticket in the worktree, reviewers are clean unless
/// scripted otherwise, and the tester passes. Every turn is journaled.
/// </summary>
internal sealed class ScriptedAgentRunner(IServiceScopeFactory scopes, FakeGitHubIssues issues, AgentScript script, AgentJournal journal) : IAgentRunner
{
    private static readonly Signature Agent = new("Fake Agent", "agent@example.invalid", new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken) => RunAsync(request, resumed: false, cancellationToken);

    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken) => RunAsync(request, resumed: true, cancellationToken);

    public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<AgentRunResult> RunAsync(AgentRunRequest request, bool resumed, CancellationToken cancellationToken)
    {
        (StepKind kind, int? ticketIssue) = await DescribeStepAsync(request.StepRunId, cancellationToken);
        bool implementing = kind is StepKind.Implement or StepKind.Fix;
        int concurrent = implementing ? journal.EnterImplementer() : journal.ActiveImplementers;
        try
        {
            journal.Record(new AgentCall(
                request.Role, kind, ticketIssue, resumed, request.Policy.RequiresGitHubWriteToken, ClosedTickets(), concurrent));
            return request.Role switch
            {
                AgentRole.Explorer => AgentRunResult.Reported(Explore(request.Policy.Paths)),
                AgentRole.Implementer => await ImplementAsync(request, ticketIssue!.Value, cancellationToken),
                AgentRole.ReviewerCodingStandards => AgentRunResult.Reported(ReviewCodingStandards(ticketIssue)),
                AgentRole.ReviewerSpecification => AgentRunResult.Reported(ReviewReport.Clean(FindingAxis.Specification, "Matches the specification.")),
                AgentRole.Tester => AgentRunResult.Reported(new TestReport(
                    TestVerdict.Pass, "Every scenario passed.", [new TestScenario("Open the app", TestScenarioOutcome.Passed)], [])),
                _ => AgentRunResult.NotReported(AgentRunOutcome.Failed, $"The fake has no script for {request.Role}."),
            };
        }
        finally
        {
            if (implementing)
            {
                journal.LeaveImplementer();
            }
        }
    }

    private static ExplorationReport Explore(PathConfinement paths)
    {
        string notesDirectory = paths.WritableRoots.FirstOrDefault(root => root != paths.WorkingDirectory) ?? paths.WorkingDirectory;
        Directory.CreateDirectory(notesDirectory);
        string notes = Path.Combine(notesDirectory, "exploration.md");
        File.WriteAllText(notes, "# Exploration notes\n");
        return new ExplorationReport(ReportStatus.Completed, "Explored the repository.", [notes]);
    }

    private async Task<(StepKind Kind, int? TicketIssue)> DescribeStepAsync(StepRunId stepRunId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        StepRun step = await scope.ServiceProvider.GetRequiredService<IStepRunRepository>().GetAsync(stepRunId, cancellationToken)
            ?? throw new InvalidOperationException($"Agent started for unknown step {stepRunId}.");
        TicketRun? ticket = step.TicketRunId is { } ticketRunId
            ? await scope.ServiceProvider.GetRequiredService<ITicketRunRepository>().GetAsync(ticketRunId, cancellationToken)
            : null;
        return (step.Kind, ticket?.Issue.Number);
    }

    private async Task<AgentRunResult> ImplementAsync(AgentRunRequest request, int ticketIssue, CancellationToken cancellationToken)
    {
        if (script.HangingImplementers.Contains(ticketIssue))
        {
            script.HangingImplementerStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        await script.ConcurrentImplementers.ArriveAsync(ticketIssue, cancellationToken);
        string worktree = request.Policy.Paths.WorkingDirectory;
        using var repository = new Repository(worktree);
        MergeIntegrationBranch(repository);
        int turn = journal.Calls.Count(call => call.TicketIssue == ticketIssue && call.Role == AgentRole.Implementer);
        File.WriteAllText(Path.Combine(worktree, $"ticket-{ticketIssue}.txt"), $"ticket {ticketIssue}, turn {turn}\n");
        Commands.Stage(repository, "*");
        Commit commit = repository.Commit($"Implement #{ticketIssue} (turn {turn})", Agent, Agent);
        return AgentRunResult.Reported(ImplementationReport.Completed(new CommitSha(commit.Sha), $"Implemented #{ticketIssue}."));
    }

    /// <summary>Ticket branches are named <c>webdevloop/&lt;run&gt;/ticket/&lt;ticket&gt;</c>; the run's integration branch is shared by the clone.</summary>
    private static void MergeIntegrationBranch(Repository repository)
    {
        string runId = repository.Head.FriendlyName.Split('/')[1];
        Branch integration = repository.Branches[$"webdevloop/{runId}/integration"]
            ?? throw new InvalidOperationException($"Integration branch of run '{runId}' is missing.");
        if (repository.ObjectDatabase.FindMergeBase(integration.Tip, repository.Head.Tip)?.Sha != integration.Tip.Sha)
        {
            repository.Merge(integration, Agent, new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward });
        }
    }

    private ReviewReport ReviewCodingStandards(int? ticketIssue)
    {
        bool firstReview = journal.Calls.Count(call => call.TicketIssue == ticketIssue && call.Role == AgentRole.ReviewerCodingStandards) == 1;
        return ticketIssue is { } issue && firstReview && script.CodingStandardsFindingsOnFirstReview.Contains(issue)
            ? ReviewReport.IssuesFound(FindingAxis.CodingStandards, "One blocking finding.", [new CodingStandardsFinding(
                CodingStandardsSeverity.Blocking, $"ticket-{issue}.txt", 1, "turn 0", "naming", "Unclear content.", "Rewrite it.")])
            : ReviewReport.Clean(FindingAxis.CodingStandards, "Follows the coding standards.");
    }

    private IReadOnlySet<int> ClosedTickets() =>
        journal.KnownTickets.Where(number => issues.StateOf(number) == IssueState.Closed).ToHashSet();
}

/// <summary>Shared across app hosts of one scenario, so a restarted host continues the same journal.</summary>
internal sealed class AgentJournal
{
    private readonly ConcurrentQueue<AgentCall> _calls = new();
    private int _activeImplementers;
    private int _peakImplementers;

    public IReadOnlyCollection<int> KnownTickets { get; init; } = [];

    public IReadOnlyList<AgentCall> Calls => [.. _calls];

    public int ActiveImplementers => Volatile.Read(ref _activeImplementers);

    public int PeakImplementers => Volatile.Read(ref _peakImplementers);

    public void Record(AgentCall call) => _calls.Enqueue(call);

    public int EnterImplementer()
    {
        int active = Interlocked.Increment(ref _activeImplementers);
        int peak;
        while (active > (peak = Volatile.Read(ref _peakImplementers)) && Interlocked.CompareExchange(ref _peakImplementers, active, peak) != peak)
        {
        }

        return active;
    }

    public void LeaveImplementer() => Interlocked.Decrement(ref _activeImplementers);
}
