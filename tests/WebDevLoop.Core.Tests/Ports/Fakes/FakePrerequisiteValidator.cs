using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class FakePrerequisiteValidator(params PrerequisiteCheck[] checks) : IPrerequisiteValidator
{
    public Task<PrerequisiteReport> ValidateAsync(CancellationToken cancellationToken) => Task.FromResult(new PrerequisiteReport(checks));
}
