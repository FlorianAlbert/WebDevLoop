using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.Git;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
