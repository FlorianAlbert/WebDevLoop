using System.Net.Sockets;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>An exception that knows whether trying the same call again later may succeed (network, rate limiting, 5xx).</summary>
public interface ITransientFault
{
    bool IsTransient { get; }
}

/// <summary>Tells temporary GitHub and network failures from permanent ones, so only the former are retried with backoff.</summary>
public static class TransientFaults
{
    private static readonly string[] TransientMessageMarkers =
    [
        "timed out",
        "timeout",
        "could not resolve host",
        "failed to connect",
        "connection reset",
        "connection refused",
        "temporary failure",
        "unexpected http status code: 5",
        "rate limit",
    ];

    public static bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ITransientFault fault)
            {
                return fault.IsTransient;
            }

            if (current is HttpRequestException or SocketException or IOException or TimeoutException)
            {
                return true;
            }
        }

        return TransientMessageMarkers.Any(marker => exception.Message.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
