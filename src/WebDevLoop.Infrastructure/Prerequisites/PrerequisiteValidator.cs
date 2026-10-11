using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>
/// Reports checks as data, honoring their declared dependencies while unrelated checks run concurrently.
/// An unexpected exception in a check becomes a failed result.
/// </summary>
public sealed class PrerequisiteValidator(IEnumerable<IPrerequisiteCheck> checks) : IPrerequisiteValidator
{
    private readonly IReadOnlyList<IPrerequisiteCheck> _checks = checks.ToArray();

    public async Task<PrerequisiteReport> ValidateAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, Task<PrerequisiteCheck>> tasks = new(StringComparer.Ordinal);
        List<IPrerequisiteCheck> pending = [.. _checks];
        while (pending.Count > 0)
        {
            int before = pending.Count;
            foreach (IPrerequisiteCheck check in pending.ToArray())
            {
                if (check.RunsAfter is null)
                {
                    tasks.Add(check.Name, RunSafelyAsync(check, cancellationToken));
                }
                else if (tasks.TryGetValue(check.RunsAfter, out Task<PrerequisiteCheck>? prerequisite))
                {
                    tasks.Add(check.Name, RunAfterPrerequisiteAsync(check, prerequisite, cancellationToken));
                }
                else
                {
                    continue;
                }

                pending.Remove(check);
            }

            if (pending.Count == before)
            {
                foreach (IPrerequisiteCheck check in pending)
                {
                    tasks.Add(check.Name, Task.FromResult(CheckResult.Failed(check.Name,
                        $"The prerequisite dependency '{check.RunsAfter}' is missing or cyclic.",
                        "Correct the prerequisite check registration.")));
                }

                break;
            }
        }

        return new PrerequisiteReport(await Task.WhenAll(_checks.Select(check => tasks[check.Name])));
    }

    private static async Task<PrerequisiteCheck> RunAfterPrerequisiteAsync(
        IPrerequisiteCheck check,
        Task<PrerequisiteCheck> prerequisite,
        CancellationToken cancellationToken)
    {
        PrerequisiteCheck result = await prerequisite;
        return result.Status != PrerequisiteStatus.Failed
            ? await RunSafelyAsync(check, cancellationToken)
            : CheckResult.Warning(check.Name, $"Waiting for prerequisite '{result.Name}' to pass.",
                $"Resolve the '{result.Name}' check first.");
    }

    private static async Task<PrerequisiteCheck> RunSafelyAsync(IPrerequisiteCheck check, CancellationToken cancellationToken)
    {
        try
        {
            return await check.RunAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CheckResult.Failed(
                check.Name,
                $"The check could not complete: {exception.Message}",
                "Fix the reported problem and re-run the prerequisite check.");
        }
    }
}
