using WebDevLoop.Infrastructure;

namespace WebDevLoop.Infrastructure.Tests;

public sealed class InfrastructureSmokeTests
{
    [Fact]
    public void project_loads_infrastructure_assembly()
    {
        Assert.Equal("WebDevLoop.Infrastructure", typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name);
    }
}
