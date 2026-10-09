using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

internal sealed class TestClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
