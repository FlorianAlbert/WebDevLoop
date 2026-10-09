using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class TestPortRangeCheck(PrerequisiteOptions options) : IPrerequisiteCheck
{
    public const string CheckName = "Test port range";

    private const int FirstUnprivilegedPort = 1024;

    private const string ConfigureRemediation = "Configure a test port range such as 41000-41099 in the settings profile.";

    public string Name => CheckName;

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken) => Task.FromResult(Evaluate());

    private PrerequisiteCheck Evaluate()
    {
        // default(TestPortRange) bypasses the constructor validation, so it reads as 0-0.
        if (options.TestPortRange is not { Start: >= TestPortRange.MinPort } range)
        {
            return CheckResult.Failed(Name, "No valid test port range is configured.", ConfigureRemediation);
        }

        string description = $"{range.Start}-{range.End}";
        return range.Start < FirstUnprivilegedPort
            ? CheckResult.Warning(
                Name,
                $"Test port range {description} includes privileged ports that the app user may not be able to bind.",
                ConfigureRemediation)
            : CheckResult.Passed(Name, $"Test port range {description} is configured.");
    }
}
