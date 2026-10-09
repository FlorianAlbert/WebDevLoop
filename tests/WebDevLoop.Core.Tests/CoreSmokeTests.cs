using WebDevLoop.Core;

namespace WebDevLoop.Core.Tests;

public sealed class CoreSmokeTests
{
    [Fact]
    public void project_loads_core_marker_type()
    {
        Assert.Equal("WebDevLoop.Core", typeof(CoreAssemblyMarker).Assembly.GetName().Name);
    }
}
