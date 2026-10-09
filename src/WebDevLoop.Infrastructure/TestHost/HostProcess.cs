namespace WebDevLoop.Infrastructure.TestHost;

/// <param name="LeaseMarker">The process's <see cref="TestTargetEnvironment.LeaseVariable"/>; null when unset or unreadable.</param>
internal sealed record HostProcess(int Id, int ParentId, int ProcessGroupId, string? LeaseMarker);
