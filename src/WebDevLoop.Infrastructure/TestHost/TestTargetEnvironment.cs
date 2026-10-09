using System.Globalization;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.TestHost;

/// <summary>
/// Variables put into every shell of a tester session. Besides the reserved port and URL, the lease marker tags every
/// process the tester starts (processes inherit it), so leftovers can be found and killed — even after an app restart.
/// </summary>
public static class TestTargetEnvironment
{
    /// <summary>The conventional variable many web frameworks read their listening port from.</summary>
    public const string PortVariable = "PORT";
    public const string ReservedPortVariable = "WEBDEVLOOP_RESERVED_PORT";
    public const string AppUrlVariable = "WEBDEVLOOP_APP_URL";
    public const string LeaseVariable = "WEBDEVLOOP_TEST_LEASE";

    public static string LeaseMarker(RunId specRunId, int port) => string.Create(CultureInfo.InvariantCulture, $"{specRunId}:{port}");

    public static Uri AppUrl(int port) => new(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}/"));

    internal static IReadOnlyDictionary<string, string> For(RunId specRunId, int port)
    {
        string number = port.ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PortVariable] = number,
            [ReservedPortVariable] = number,
            [AppUrlVariable] = AppUrl(port).ToString(),
            [LeaseVariable] = LeaseMarker(specRunId, port),
        };
    }
}
