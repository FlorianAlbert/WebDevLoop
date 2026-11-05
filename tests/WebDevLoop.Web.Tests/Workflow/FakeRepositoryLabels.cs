using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

internal sealed class FakeRepositoryLabels : IGitHubRepositoryLabels
{
    private readonly object _sync = new();
    private bool _exists = true;
    private TaskCompletionSource? _pendingGate;
    private TaskCompletionSource? _creationGate;

    public bool Exists
    {
        get
        {
            lock (_sync)
            {
                return _exists;
            }
        }
        set
        {
            lock (_sync)
            {
                _exists = value;
            }
        }
    }

    public bool FailLookup { get; set; }

    public bool FailCreation { get; set; }

    /// <summary>Holds lookups that observe the next created label until the returned action runs, so tests can act between creation and its first observation.</summary>
    public Action HoldLookupsAfterNextCreation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _pendingGate = gate;
        }

        return () => gate.TrySetResult();
    }

    public async Task<bool> HasOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        bool exists;
        TaskCompletionSource? gate;
        lock (_sync)
        {
            exists = _exists;
            gate = exists ? _creationGate : null;
        }

        if (gate is not null)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return FailLookup ? throw new RepositoryLabelException("Check repository permissions and retry.") : exists;
    }

    public Task CreateOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        if (FailCreation)
        {
            throw new RepositoryLabelException("Check Issues write permissions and retry.");
        }

        lock (_sync)
        {
            _exists = true;
            _creationGate = _pendingGate;
            _pendingGate = null;
        }

        return Task.CompletedTask;
    }
}
