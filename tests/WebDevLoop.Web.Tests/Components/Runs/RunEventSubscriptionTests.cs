using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Runs;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Runs;

public sealed class RunEventSubscriptionTests
{
    [Fact]
    public async Task Relevant_event_triggers_a_reload()
    {
        var bus = Events.NewBus();
        int reloads = 0;
        using var subscription = new RunEventSubscription(bus, view => RunEventSubscription.Concerns(view, "run-1"), () => { reloads++; return Task.CompletedTask; });

        await bus.PublishAsync(Events.TicketStatus("run-1", "t1", TicketRunStatus.Blocked, TicketRunStatus.Ready));

        Assert.Equal(1, reloads);
    }

    [Fact]
    public async Task Event_of_another_spec_run_is_ignored()
    {
        var bus = Events.NewBus();
        int reloads = 0;
        using var subscription = new RunEventSubscription(bus, view => RunEventSubscription.Concerns(view, "run-1"), () => { reloads++; return Task.CompletedTask; });

        await bus.PublishAsync(Events.TicketStatus("run-2", "t9", TicketRunStatus.Blocked, TicketRunStatus.Ready));

        Assert.Equal(0, reloads);
    }

    [Fact]
    public async Task Event_type_unknown_to_the_live_mapper_reloads_to_stay_correct()
    {
        var bus = Events.NewBus();
        int reloads = 0;
        using var subscription = new RunEventSubscription(bus, view => RunEventSubscription.Concerns(view, "run-1"), () => { reloads++; return Task.CompletedTask; });

        await bus.PublishAsync(new Events.UnmappedEvent());

        Assert.Equal(1, reloads);
    }

    [Fact]
    public void Everything_is_relevant_while_the_spec_run_is_not_yet_known()
    {
        LiveEventView view = new(1, "TicketRunStatusChanged", "run-7", "t1", null, "Ready", Views.Now);

        Assert.True(RunEventSubscription.Concerns(view, null));
    }

    [Fact]
    public async Task Events_during_a_running_reload_collapse_into_one_follow_up_reload()
    {
        var bus = Events.NewBus();
        int started = 0;
        var gate = new TaskCompletionSource();
        Task Reload()
        {
            return Interlocked.Increment(ref started) == 1 ? gate.Task : Task.CompletedTask;
        }

        using var subscription = new RunEventSubscription(bus, _ => true, Reload);
        Task first = bus.PublishAsync(Events.StepStatus("run-1", "t1", "s1", StepStatus.Running));
        await bus.PublishAsync(Events.StepStatus("run-1", "t1", "s1", StepStatus.Succeeded));
        await bus.PublishAsync(Events.StepStatus("run-1", "t1", "s2", StepStatus.Running));
        Assert.Equal(1, started);

        gate.SetResult();
        await first;

        Assert.Equal(2, started);
    }

    [Fact]
    public async Task Disposed_subscription_no_longer_reloads()
    {
        var bus = Events.NewBus();
        int reloads = 0;
        var subscription = new RunEventSubscription(bus, _ => true, () => { reloads++; return Task.CompletedTask; });
        subscription.Dispose();

        await bus.PublishAsync(Events.StepStatus("run-1", "t1", "s1", StepStatus.Running));

        Assert.Equal(0, reloads);
    }
}
