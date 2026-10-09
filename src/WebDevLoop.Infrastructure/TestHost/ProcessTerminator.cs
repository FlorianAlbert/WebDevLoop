using System.ComponentModel;
using System.Diagnostics;

namespace WebDevLoop.Infrastructure.TestHost;

internal sealed class ProcessTerminator : IProcessTerminator
{
    public bool Kill(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
