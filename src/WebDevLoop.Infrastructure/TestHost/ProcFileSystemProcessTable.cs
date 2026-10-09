using System.Globalization;
using System.Text;

namespace WebDevLoop.Infrastructure.TestHost;

/// <summary>
/// Reads processes from Linux <c>/proc</c>: parent and process group from <c>stat</c>, the lease marker from
/// <c>environ</c> (readable for this user's processes only, which are the only ones the tester can start). On systems
/// without <c>/proc</c> the snapshot is empty, so no leftovers are found.
/// </summary>
internal sealed class ProcFileSystemProcessTable : IProcessTable
{
    private const string ProcRoot = "/proc";
    private const int ParentIdField = 1;
    private const int ProcessGroupField = 2;

    private static readonly string LeasePrefix = TestTargetEnvironment.LeaseVariable + "=";

    public int CurrentProcessId => Environment.ProcessId;

    public IReadOnlyList<HostProcess> Snapshot()
    {
        if (!Directory.Exists(ProcRoot))
        {
            return [];
        }

        var snapshot = new List<HostProcess>();
        foreach (string directory in Directory.EnumerateDirectories(ProcRoot))
        {
            if (int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
                && Read(directory, processId) is { } process)
            {
                snapshot.Add(process);
            }
        }

        return snapshot;
    }

    /// <returns>Null when the process exited while it was being read.</returns>
    private static HostProcess? Read(string directory, int processId)
    {
        try
        {
            // stat is "pid (comm) state ppid pgrp ..."; comm may contain spaces and parentheses, so parse after the last ')'.
            string stat = File.ReadAllText(Path.Combine(directory, "stat"));
            string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return new HostProcess(
                processId,
                int.Parse(fields[ParentIdField], CultureInfo.InvariantCulture),
                int.Parse(fields[ProcessGroupField], CultureInfo.InvariantCulture),
                ReadLeaseMarker(directory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ReadLeaseMarker(string directory)
    {
        try
        {
            string environment = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "environ")));
            return environment.Split('\0').FirstOrDefault(variable => variable.StartsWith(LeasePrefix, StringComparison.Ordinal))?[LeasePrefix.Length..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
