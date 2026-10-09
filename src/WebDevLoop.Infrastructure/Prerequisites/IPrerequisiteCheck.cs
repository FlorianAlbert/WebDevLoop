using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>One external prerequisite. Reports problems as a failed or warning result rather than throwing.</summary>
public interface IPrerequisiteCheck
{
    string Name { get; }

    Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken);
}
