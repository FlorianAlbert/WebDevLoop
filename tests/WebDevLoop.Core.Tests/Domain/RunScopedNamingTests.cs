using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain;

public sealed class RunScopedNamingTests
{
    private static readonly RunId Run = new("run1");
    private static readonly TicketRunId Ticket = new("t7");

    [Fact]
    public void integration_branch_is_scoped_to_the_run()
    {
        Assert.Equal("webdevloop/run1/integration", RunScopedNaming.IntegrationBranch(Run).Value);
    }

    [Fact]
    public void stack_branch_is_scoped_to_run_and_ticket()
    {
        Assert.Equal("stack/run1/t7", RunScopedNaming.StackBranch(Run, Ticket).Value);
    }

    [Fact]
    public void ticket_branch_is_scoped_to_run_and_ticket()
    {
        Assert.Equal("webdevloop/run1/ticket/t7", RunScopedNaming.TicketBranch(Run, Ticket).Value);
    }

    [Fact]
    public void repeated_runs_for_the_same_ticket_get_distinct_stack_branch_names()
    {
        BranchName first = RunScopedNaming.StackBranch(new RunId("run1"), Ticket);
        BranchName second = RunScopedNaming.StackBranch(new RunId("run2"), Ticket);

        Assert.NotEqual(first, second);
    }
}
