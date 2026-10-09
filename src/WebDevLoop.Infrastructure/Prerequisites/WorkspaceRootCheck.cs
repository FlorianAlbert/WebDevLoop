using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class WorkspaceRootCheck(PrerequisiteOptions options, IFileSystemProbe fileSystem) : IPrerequisiteCheck
{
    public const string CheckName = "Workspace root";

    public string Name => CheckName;

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        string? problem = fileSystem.TryEnsureWritableDirectory(options.WorkspaceRoot);
        return Task.FromResult(problem is null
            ? CheckResult.Passed(Name, $"Workspace root '{options.WorkspaceRoot}' is writable.")
            : CheckResult.Failed(
                Name,
                $"Workspace root '{options.WorkspaceRoot}' is not usable: {problem}",
                "Create the directory (or change the workspace root setting) and grant the app user write access."));
    }
}
