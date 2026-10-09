using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.Events;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;
}
