namespace WebDevLoop.Infrastructure.TestHost;

internal interface IProcessTable
{
    int CurrentProcessId { get; }

    /// <summary>The processes of this machine that this user may inspect.</summary>
    IReadOnlyList<HostProcess> Snapshot();
}
