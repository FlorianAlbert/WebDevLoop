namespace WebDevLoop.Core.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
