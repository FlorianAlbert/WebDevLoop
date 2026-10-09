using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.TestHost;

namespace WebDevLoop.Infrastructure.Tests.TestHost;

public sealed class TestHostRegistrationTests
{
    [Fact]
    public void one_shared_test_target_runner_holds_every_port_reservation()
    {
        ServiceProvider provider = new ServiceCollection().AddTestHost().BuildServiceProvider(validateScopes: true);

        ITestTargetRunner runner = provider.GetRequiredService<ITestTargetRunner>();

        Assert.IsType<TestTargetRunner>(runner);
        Assert.Same(runner, provider.GetRequiredService<ITestTargetRunner>());
    }
}
