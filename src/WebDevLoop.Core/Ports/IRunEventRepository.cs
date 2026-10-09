using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Audit log of run events shown in the UI and API.</summary>
public interface IRunEventRepository
{
    Task<IReadOnlyList<RunEvent>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken);

    void Add(RunEvent runEvent);
}
