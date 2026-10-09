using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;

namespace WebDevLoop.Web.Tests.Api;

/// <summary>Records control commands and answers with <see cref="Result"/>; <see cref="OnApplied"/> lets a test update its fake projections.</summary>
internal sealed class FakeRunControl : IRunControl
{
    private readonly Lock _gate = new();
    private readonly List<(string Command, string Id, SkipDependents? Dependents)> _calls = [];

    public ControlResult Result { get; set; } = ControlResult.Applied();

    /// <summary>Invoked with (command, id) when <see cref="Result"/> is applied, e.g. to change the run the endpoint reads back.</summary>
    public Action<string, string>? OnApplied { get; set; }

    public IReadOnlyList<(string Command, string Id, SkipDependents? Dependents)> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    public Task<ControlResult> RetrySpecAsync(RunId specRunId, CancellationToken cancellationToken) => Record("retry-spec", specRunId.Value);

    public Task<ControlResult> AbortSpecAsync(RunId specRunId, CancellationToken cancellationToken) => Record("abort-spec", specRunId.Value);

    public Task<ControlResult> RetryTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) => Record("retry-ticket", ticketRunId.Value);

    public Task<ControlResult> SkipTicketAsync(TicketRunId ticketRunId, SkipDependents dependents, CancellationToken cancellationToken) =>
        Record("skip-ticket", ticketRunId.Value, dependents);

    public Task<ControlResult> AbortTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) => Record("abort-ticket", ticketRunId.Value);

    private Task<ControlResult> Record(string command, string id, SkipDependents? dependents = null)
    {
        lock (_gate)
        {
            _calls.Add((command, id, dependents));
        }

        if (Result.IsApplied)
        {
            OnApplied?.Invoke(command, id);
        }

        return Task.FromResult(Result);
    }
}
