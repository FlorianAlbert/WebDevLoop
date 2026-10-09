using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Runs every check and reports the outcome as data; an unexpected exception in a check becomes a failed result.</summary>
public sealed class PrerequisiteValidator(IEnumerable<IPrerequisiteCheck> checks) : IPrerequisiteValidator
{
    private readonly IReadOnlyList<IPrerequisiteCheck> _checks = checks.ToArray();

    public async Task<PrerequisiteReport> ValidateAsync(CancellationToken cancellationToken)
    {
        PrerequisiteCheck[] results = await Task.WhenAll(_checks.Select(check => RunSafelyAsync(check, cancellationToken)));
        return new PrerequisiteReport(results);
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
