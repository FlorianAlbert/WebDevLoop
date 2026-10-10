using System.ComponentModel;
using System.Diagnostics;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class ProcessProbe(TimeSpan timeout) : IProcessProbe
{
    public async Task<ProcessProbeResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? ResolveWindowsExecutable(executable) : executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        }
        catch (Win32Exception)
        {
            return ProcessProbeResult.NotFound;
        }

        using (process)
        {
            process.StandardInput.Close();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
                Task<string> error = process.StandardError.ReadToEndAsync(timeoutSource.Token);
                await process.WaitForExitAsync(timeoutSource.Token);
                string text = (await output).Trim();
                return new ProcessProbeResult(
                    ProcessProbeOutcome.Completed,
                    process.ExitCode,
                    text.Length > 0 ? FirstLine(text) : FirstLine((await error).Trim()));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return ProcessProbeResult.TimedOut;
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
        }
    }

    private static string ResolveWindowsExecutable(string executable)
    {
        string[] extensions = Path.HasExtension(executable)
            ? [string.Empty]
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<string> directories = Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar)
            || executable.Contains(Path.AltDirectorySeparatorChar)
            ? [string.Empty]
            : new[] { Environment.CurrentDirectory }.Concat(
                (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (string directory in directories)
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(directory.Trim('"'), executable + extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return executable;
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].TrimEnd('\r');
}
