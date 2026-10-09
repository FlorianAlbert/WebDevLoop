using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

public static class TestLeaseTarget
{
    /// <summary>The target to stop for a lease: the supervisor identifies its processes by spec run and port; URL and environment are not needed.</summary>
    public static TestTarget Of(TestLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new TestTarget(
            lease.SpecRunId,
            lease.Port,
            new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{lease.Port}/")),
            new Dictionary<string, string>());
    }
}
