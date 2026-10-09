using WebDevLoop.Core.Orchestration.Preparation;

namespace WebDevLoop.Core.Tests.Orchestration.Preparation;

public sealed class RecordingDirectoryProvisioner : IAppDirectoryProvisioner
{
    public List<string> Created { get; } = [];

    public void EnsureExists(string absolutePath) => Created.Add(absolutePath);
}
