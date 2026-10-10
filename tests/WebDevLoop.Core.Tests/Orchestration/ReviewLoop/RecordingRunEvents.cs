using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

internal sealed class RecordingRunEvents : IRunEventRepository
{
    private readonly List<RunEvent> _events = [];

    public IReadOnlyList<RunEvent> All => _events;

    public Task<IReadOnlyList<RunEvent>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunEvent>>([.. _events.Where(runEvent => runEvent.SpecRunId == specRunId)]);

    public void Add(RunEvent runEvent) => _events.Add(runEvent);
}
