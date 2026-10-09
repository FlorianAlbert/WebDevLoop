namespace WebDevLoop.Core.Ports;

/// <param name="Global">Occupied implementer slots across all repositories.</param>
/// <param name="InRepository">Occupied implementer slots of the requested repository.</param>
public sealed record ImplementerSlotUsage(int Global, int InRepository);
