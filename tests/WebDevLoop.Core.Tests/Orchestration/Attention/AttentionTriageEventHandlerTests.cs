using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Attention;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.Attention;

public sealed class AttentionTriageEventHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly RecordingLauncher _launcher = new();
    private readonly InMemoryWorkflowStore _store = new();

    [Fact]
    public async Task a_ticket_entering_needs_attention_starts_the_resolution_pipeline_for_that_ticket()
    {
        await Handle(new TicketRunStatusChanged(new RunId("r1"), new TicketRunId("t1"), TicketRunStatus.Reviewing, TicketRunStatus.NeedsAttention, T0));

        Assert.Equal(new AttentionTriageAssignment(new RunId("r1"), new TicketRunId("t1")), Assert.Single(_launcher.Launched));
    }

    [Fact]
    public async Task a_run_entering_needs_attention_starts_the_resolution_pipeline_for_the_run()
    {
        await Handle(new SpecRunStatusChanged(new RunId("r1"), 1, SpecRunStatus.Preparing, SpecRunStatus.NeedsAttention, T0));

        Assert.Equal(new AttentionTriageAssignment(new RunId("r1"), null), Assert.Single(_launcher.Launched));
    }

    [Fact]
    public async Task other_transitions_are_ignored()
    {
        await Handle(new TicketRunStatusChanged(new RunId("r1"), new TicketRunId("t1"), TicketRunStatus.NeedsAttention, TicketRunStatus.Reviewing, T0));
        await Handle(new SpecRunStatusChanged(new RunId("r1"), 1, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, T0));

        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public async Task a_reconciliation_pass_relaunches_the_pipeline_for_items_still_waiting_for_their_automatic_fix()
    {
        SpecRun spec = SpecRun.Queue(new RunId("r1"), 1, new IssueRef("octo", "app", 1), "Spec", "body", 1, T0);
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.TransitionTo(SpecRunStatus.Running, T0);
        _store.Add(spec);
        TicketRun waiting = Ticket(spec, "t1", AttentionReasons.WorktreeNotClean("/w", "b", "dirty"));
        TicketRun asked = Ticket(spec, "t2", AttentionReasons.WorktreeNotClean("/w", "b", "dirty").WithAutoFixAttempted("still dirty"));
        TicketRun forUser = Ticket(spec, "t3", AttentionReasons.BaseBranchMissing("trunk", "octo/app", "missing"));

        await Handle(new FrontierReconciliationRequested(spec.Id, T0));

        Assert.Equal(new AttentionTriageAssignment(spec.Id, waiting.Id), Assert.Single(_launcher.Launched));
        Assert.DoesNotContain(_launcher.Launched, assignment => assignment.TicketRunId == asked.Id || assignment.TicketRunId == forUser.Id);
    }

    private TicketRun Ticket(SpecRun spec, string id, AttentionReason reason)
    {
        TicketRun ticket = TicketRun.Create(new TicketRunId(id), spec.Id, new IssueRef("octo", "app", 10), "Ticket", "body", T0);
        ticket.TransitionTo(TicketRunStatus.Ready, T0);
        ticket.TransitionTo(TicketRunStatus.Implementing, T0);
        ticket.TransitionTo(TicketRunStatus.Reviewing, T0);
        ticket.MarkNeedsAttention(reason, T0);
        _store.Add(ticket);
        return ticket;
    }

    private Task Handle(WorkflowEvent workflowEvent) =>
        new AttentionTriageEventHandler(_launcher, _store, _store).HandleAsync(new EventEnvelope(1, workflowEvent), TestContext.Current.CancellationToken);

    private sealed class RecordingLauncher : IAttentionTriageLauncher
    {
        public List<AttentionTriageAssignment> Launched { get; } = [];

        public void Launch(AttentionTriageAssignment assignment) => Launched.Add(assignment);
    }
}

public sealed class TransientFaultsTests
{
    [Fact]
    public void network_timeouts_and_http_failures_are_transient()
    {
        Assert.True(TransientFaults.IsTransient(new HttpRequestException("503 Service Unavailable")));
        Assert.True(TransientFaults.IsTransient(new IOException("Broken pipe")));
        Assert.True(TransientFaults.IsTransient(new TimeoutException()));
        Assert.True(TransientFaults.IsTransient(new InvalidOperationException("outer", new HttpRequestException("inner"))));
    }

    [Fact]
    public void git_transport_failures_are_recognised_by_their_message()
    {
        Assert.True(TransientFaults.IsTransient(new InvalidOperationException("failed to connect to github.com: Connection timed out")));
        Assert.True(TransientFaults.IsTransient(new InvalidOperationException("unexpected http status code: 502")));
    }

    [Fact]
    public void permission_and_validation_failures_are_not_transient()
    {
        Assert.False(TransientFaults.IsTransient(new InvalidOperationException("Resource not accessible by integration")));
        Assert.False(TransientFaults.IsTransient(new ArgumentException("bad input")));
    }

    [Fact]
    public void an_exception_that_knows_better_decides_for_itself()
    {
        Assert.False(TransientFaults.IsTransient(new Declared(false, "timed out")));
        Assert.True(TransientFaults.IsTransient(new Declared(true, "forbidden")));
    }

    private sealed class Declared(bool transient, string message) : Exception(message), ITransientFault
    {
        public bool IsTransient => transient;
    }
}
