using System.Text.Json;
using WebDevLoop.Core.Management;

namespace WebDevLoop.Infrastructure.Management;

/// <summary>
/// The UI's current repository, persisted in a small JSON file in the app data directory so it survives restarts. It is
/// view context only, so an unreadable file simply starts without a selection.
/// </summary>
public sealed class FileCurrentRepositorySelection : ICurrentRepositorySelection
{
    private readonly string _statePath;
    private readonly object _gate = new();
    private int? _current;

    public FileCurrentRepositorySelection(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = statePath;
        _current = Load(statePath);
    }

    public int? CurrentRepositoryId
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Select(int? repositoryId)
    {
        lock (_gate)
        {
            _current = repositoryId;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_statePath))!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(new SelectionState(repositoryId)));
        }
    }

    private static int? Load(string statePath)
    {
        try
        {
            return File.Exists(statePath) ? JsonSerializer.Deserialize<SelectionState>(File.ReadAllText(statePath))?.CurrentRepositoryId : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record SelectionState(int? CurrentRepositoryId);
}
