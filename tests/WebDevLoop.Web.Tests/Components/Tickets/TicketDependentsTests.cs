using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Runs;
using WebDevLoop.Web.Components.Tickets;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Tickets;

public sealed class TicketDependentsTests
{
    // t1 (#10, needs attention) <- t2 (#11, blocked) <- t3 (#12, blocked) <- t7 (#16, blocked), and t4 (#13, implementing) <- t5 (#14, blocked); t6 (#15) is unrelated.
    private static readonly TicketRunView T1 = Views.Ticket("t1", 10, TicketRunStatus.NeedsAttention);

    private static readonly IReadOnlyList<TicketRunView> All =
    [
        T1,
        Views.Ticket("t2", 11, TicketRunStatus.Blocked, blockedBy: "t1"),
        Views.Ticket("t3", 12, TicketRunStatus.Blocked, blockedBy: "t2"),
        Views.Ticket("t4", 13, TicketRunStatus.Implementing, blockedBy: "t1"),
        Views.Ticket("t5", 14, TicketRunStatus.Blocked, blockedBy: "t4"),
        Views.Ticket("t6", 15, TicketRunStatus.Ready),
        Views.Ticket("t7", 16, TicketRunStatus.Blocked, blockedBy: "t3"),
    ];

    private static string[] Ids(IEnumerable<TicketRunView> tickets) => tickets.Select(ticket => ticket.Id).ToArray();

    [Fact]
    public void Direct_dependents_are_the_tickets_that_list_the_ticket_as_a_blocker()
    {
        Assert.Equal(["t2", "t4"], Ids(TicketDependents.Direct(T1, All)));
    }

    [Fact]
    public void Skip_releases_only_direct_dependents_that_are_blocked()
    {
        Assert.Equal(["t2"], Ids(TicketDependents.Released(T1, All)));
    }

    [Fact]
    public void Skip_with_dependents_follows_the_chain_but_not_past_tickets_that_are_being_worked_on()
    {
        Assert.Equal(["t2", "t3", "t7"], Ids(TicketDependents.ToSkip(T1, All)));
    }

    [Theory]
    [InlineData(TicketRunStatus.Blocked, true)]
    [InlineData(TicketRunStatus.Ready, true)]
    [InlineData(TicketRunStatus.NeedsAttention, true)]
    [InlineData(TicketRunStatus.Implementing, false)]
    [InlineData(TicketRunStatus.Integrated, false)]
    [InlineData(TicketRunStatus.Skipped, false)]
    [InlineData(TicketRunStatus.Aborted, false)]
    public void Only_tickets_that_are_not_being_worked_on_are_skipped_with_their_blocker(TicketRunStatus status, bool skipped)
    {
        TicketRunView[] all = [T1, Views.Ticket("d", 20, status, blockedBy: "t1")];

        Assert.Equal(skipped, TicketDependents.ToSkip(T1, all).Count == 1);
    }

    [Fact]
    public void Aborting_leaves_blocked_dependents_waiting_through_chains_of_blocked_tickets()
    {
        Assert.Equal(["t2", "t3", "t7"], Ids(TicketDependents.StayBlocked(T1, All)));
    }

    [Fact]
    public void Dependency_cycles_terminate()
    {
        TicketRunView a = Views.Ticket("a", 1, TicketRunStatus.Blocked, blockedBy: "b");
        TicketRunView b = Views.Ticket("b", 2, TicketRunStatus.Blocked, blockedBy: "a");

        Assert.Equal(["b"], Ids(TicketDependents.ToSkip(a, [a, b])));
        Assert.Equal(["b"], Ids(TicketDependents.StayBlocked(a, [a, b])));
    }

    [Fact]
    public void A_ticket_without_dependents_has_none()
    {
        TicketRunView lonely = All[5];

        Assert.Empty(TicketDependents.ToSkip(lonely, All));
        Assert.Empty(TicketDependents.Released(lonely, All));
        Assert.Empty(TicketDependents.StayBlocked(lonely, All));
    }

    [Fact]
    public void Descriptions_name_the_ticket_by_number_and_shortened_title()
    {
        Assert.Equal("#11 Ticket 11, #12 Ticket 12", TicketDependents.Describe(All.Skip(1).Take(2)));
        string shortened = TicketDependents.Describe(All[0] with { Title = new string('x', 100) });
        Assert.Equal("#10 ".Length + 60, shortened.Length);
        Assert.EndsWith("…", shortened);
    }

    [Fact]
    public void Ticket_commands_name_the_affected_tickets_in_consequence_and_confirmation()
    {
        var commands = TicketRunCommands.Build(T1, All).ToDictionary(command => command.Key);

        Assert.Equal(["retry", "skip", "skip-dependents", "abort"], commands.Keys);
        Assert.Null(commands["retry"].Confirmation);
        Assert.Equal("Unblocks #11 Ticket 11.", commands["skip"].Impact);
        Assert.Contains("#11 Ticket 11", commands["skip"].Confirmation);
        Assert.Equal("Also skips #11 Ticket 11, #12 Ticket 12, #16 Ticket 16.", commands["skip-dependents"].Impact);
        Assert.Contains("#16 Ticket 16", commands["skip-dependents"].Confirmation);
        Assert.Contains("3 tickets", commands["skip-dependents"].Confirmation);
        Assert.Equal("Stay blocked: #11 Ticket 11, #12 Ticket 12, #16 Ticket 16.", commands["abort"].Impact);
        Assert.Contains("#10 Ticket 10", commands["abort"].Confirmation);
        Assert.All(commands.Values, command => Assert.False(string.IsNullOrWhiteSpace(command.Consequence)));
    }

    [Fact]
    public void Ticket_commands_say_so_when_nothing_else_is_affected()
    {
        var commands = TicketRunCommands.Build(All[5], All).ToDictionary(command => command.Key);

        Assert.Equal("No other tickets depend on this one.", commands["skip-dependents"].Impact);
        Assert.Equal("No other ticket is waiting for this one.", commands["skip"].Impact);
        Assert.Equal("No other ticket is waiting for this one.", commands["abort"].Impact);
    }

    [Fact]
    public void Ticket_commands_follow_the_buttons_and_wording_of_the_reason()
    {
        AttentionReason reason = AttentionData.Full();
        AttentionReason retryOnly = new(reason.Code, reason.Summary, reason.WhyItMatters, reason.Details, reason.Cause, null, [], [], [reason.Actions[0] with { Label = "Try again" }]);

        var commands = TicketRunCommands.Build(T1, All, retryOnly);

        var only = Assert.Single(commands);
        Assert.Equal("Try again", only.Label);
        Assert.Equal("Starts the failed phase again.", only.Consequence);
    }

    [Fact]
    public void Spec_abort_names_the_open_tickets()
    {
        SpecRunView spec = Views.Spec(status: SpecRunStatus.NeedsAttention);
        TicketRunView[] tickets = [.. All.Take(3), Views.Ticket("done", 3, TicketRunStatus.Integrated), Views.Ticket("gone", 4, TicketRunStatus.Aborted)];

        var abort = SpecRunCommands.Build(spec, tickets).Single(command => command.Key == "abort");

        Assert.Equal("Aborts 3 open tickets (#10 Ticket 10, #11 Ticket 11, #12 Ticket 12).", abort.Impact);
        Assert.Contains("3 open tickets", abort.Confirmation);
        Assert.Contains("#12 Ticket 12", abort.Confirmation);
        Assert.Equal("No tickets are open.", SpecRunCommands.Build(spec, []).Single(command => command.Key == "abort").Impact);
        Assert.Equal("no open tickets", SpecRunCommands.OpenTickets([]));
    }

    [Fact]
    public void Spec_abort_shortens_a_long_list_of_open_tickets()
    {
        TicketRunView[] many = Enumerable.Range(1, 9).Select(n => Views.Ticket($"t{n}", n, TicketRunStatus.Ready)).ToArray();

        string text = SpecRunCommands.OpenTickets(many);

        Assert.StartsWith("9 open tickets (#1 Ticket 1", text);
        Assert.EndsWith("and 3 more)", text);
    }
}
