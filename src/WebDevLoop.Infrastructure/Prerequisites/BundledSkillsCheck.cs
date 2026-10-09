using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Skills;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class BundledSkillsCheck(BundledSkillsCatalog skills) : IPrerequisiteCheck
{
    public const string CheckName = "Bundled skills";

    public string Name => CheckName;

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        SkillManifestValidation validation = skills.Validate();
        return Task.FromResult(validation.IsValid
            ? CheckResult.Passed(Name, "All bundled skills listed in the manifest are present.")
            : CheckResult.Failed(
                Name,
                string.Join(' ', validation.Errors),
                "Rebuild so the skills folder is copied to the app output, or restore the missing skill files."));
    }
}
