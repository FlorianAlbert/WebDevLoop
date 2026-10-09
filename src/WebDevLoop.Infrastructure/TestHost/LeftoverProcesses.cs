namespace WebDevLoop.Infrastructure.TestHost;

/// <summary>
/// Selects the leftover processes of one test target from a process snapshot: every process tagged with the target's lease
/// marker, plus the members of process groups led by such a process (children that dropped the marker). This process and
/// its direct children are never selected: the Copilot runtime serving the tester is our child and inherits the tester's
/// shell environment, yet must survive; after an app restart it is orphaned and treated as a leftover like the others.
/// </summary>
internal static class LeftoverProcesses
{
    public static IReadOnlyList<int> Find(IReadOnlyList<HostProcess> snapshot, int currentProcessId, string leaseMarker)
    {
        HashSet<int> protectedIds = snapshot
            .Where(process => process.ParentId == currentProcessId)
            .Select(process => process.Id)
            .Append(currentProcessId)
            .ToHashSet();
        int? ownGroup = snapshot.FirstOrDefault(process => process.Id == currentProcessId)?.ProcessGroupId;
        HostProcess[] tagged = snapshot
            .Where(process => process.LeaseMarker == leaseMarker && !protectedIds.Contains(process.Id))
            .ToArray();
        HashSet<int> ledGroups = tagged
            .Where(process => process.ProcessGroupId == process.Id && process.ProcessGroupId != ownGroup)
            .Select(process => process.ProcessGroupId)
            .ToHashSet();

        return snapshot
            .Where(process => !protectedIds.Contains(process.Id) && (process.LeaseMarker == leaseMarker || ledGroups.Contains(process.ProcessGroupId)))
            .Select(process => process.Id)
            .ToArray();
    }
}
