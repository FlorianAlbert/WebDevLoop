using WebDevLoop.Core.Management;

namespace WebDevLoop.Web.Components.Repositories;

/// <summary>
/// The UI's "current repository": wraps the shared selection and tells open pages when it (or the repository list) changed.
/// View context only: nothing here starts, stops or filters scheduling.
/// </summary>
public sealed class RepositoryContext(ICurrentRepositorySelection selection)
{
    public event Action? Changed;

    public int? CurrentRepositoryId => selection.CurrentRepositoryId;

    public void Select(int? repositoryId)
    {
        selection.Select(repositoryId);
        NotifyChanged();
    }

    /// <summary>Clears the selection if it points at a repository that no longer exists (removed, or the database was reset).</summary>
    public bool ClearIfDangling(IEnumerable<int> existingRepositoryIds)
    {
        if (CurrentRepositoryId is not { } current || existingRepositoryIds.Contains(current))
        {
            return false;
        }

        Select(null);
        return true;
    }

    public void NotifyChanged() => Changed?.Invoke();
}
