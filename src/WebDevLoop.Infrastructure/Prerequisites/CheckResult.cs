using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

internal static class CheckResult
{
    public static PrerequisiteCheck Passed(string name, string message) => new(name, PrerequisiteStatus.Passed, message);

    public static PrerequisiteCheck Warning(string name, string message, string remediation) =>
        new(name, PrerequisiteStatus.Warning, message, remediation);

    public static PrerequisiteCheck Failed(string name, string message, string remediation) =>
        new(name, PrerequisiteStatus.Failed, message, remediation);
}
