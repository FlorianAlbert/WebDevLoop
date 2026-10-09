using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.Tests.Api;

public sealed class ApplicationServiceRegistrationTests
{
    [Theory]
    [InlineData(typeof(IRunControl), typeof(RunControlService))]
    [InlineData(typeof(SpecRunControl), typeof(SpecRunControl))]
    [InlineData(typeof(TicketRunControl), typeof(TicketRunControl))]
    [InlineData(typeof(RunControlJournal), typeof(RunControlJournal))]
    [InlineData(typeof(ActiveWorkStopper), typeof(ActiveWorkStopper))]
    public void control_services_are_registered_per_scope(Type service, Type implementation)
    {
        ServiceDescriptor descriptor = Assert.Single(new ServiceCollection().AddWebDevLoopApplicationServices(), candidate => candidate.ServiceType == service);

        Assert.Equal((implementation, ServiceLifetime.Scoped), (descriptor.ImplementationType, descriptor.Lifetime));
    }

    [Fact]
    public void control_options_default_to_a_bounded_wait_for_the_merge_lock()
    {
        ServiceDescriptor descriptor = Assert.Single(new ServiceCollection().AddWebDevLoopApplicationServices(), candidate => candidate.ServiceType == typeof(RunControlOptions));

        Assert.Same(RunControlOptions.Default, descriptor.ImplementationInstance);
    }
}
