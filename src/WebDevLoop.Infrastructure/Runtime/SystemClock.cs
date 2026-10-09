using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Runtime;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
