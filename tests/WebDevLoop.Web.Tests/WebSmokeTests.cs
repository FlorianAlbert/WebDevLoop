using System.Reflection;
using WebDevLoop.Web;

namespace WebDevLoop.Web.Tests;

public sealed class WebSmokeTests
{
    [Fact]
    public void app_assembly_is_loadable()
    {
        Assembly assembly = Assembly.Load(typeof(WebAssemblyMarker).Assembly.GetName());

        Assert.Equal("WebDevLoop.Web", assembly.GetName().Name);
    }
}
