using System.Reflection;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class PortFakeCoverageTests
{
    private static readonly string[] PortNamespaces = ["WebDevLoop.Core.Ports", "WebDevLoop.Core.Events"];

    [Fact]
    public void every_core_port_has_an_in_memory_fake_built_only_from_core_types()
    {
        Assembly core = typeof(SpecRun).Assembly;
        Type[] fakes = typeof(PortFakeCoverageTests).Assembly.GetTypes()
            .Where(type => type.Namespace == "WebDevLoop.Core.Tests.Ports.Fakes" && type.IsClass && !type.IsAbstract)
            .ToArray();

        string[] portsWithoutFake = core.GetTypes()
            .Where(type => type.IsInterface && type.IsPublic && PortNamespaces.Contains(type.Namespace))
            .Where(port => !fakes.Any(port.IsAssignableFrom))
            .Select(port => port.FullName!)
            .Order()
            .ToArray();

        Assert.Empty(portsWithoutFake);
    }
}
