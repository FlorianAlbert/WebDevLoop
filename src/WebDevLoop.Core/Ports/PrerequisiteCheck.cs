namespace WebDevLoop.Core.Ports;

public sealed record PrerequisiteCheck(string Name, PrerequisiteStatus Status, string Message, string? Remediation = null);
