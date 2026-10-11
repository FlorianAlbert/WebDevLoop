using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>One external prerequisite. Reports problems as a failed or warning result rather than throwing.</summary>
public interface IPrerequisiteCheck
{
    string Name { get; }

    /// <summary>The check that must complete without failure first; warnings do not block. Null means independent.</summary>
    string? RunsAfter => null;

    Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken);
}
