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

    public void NotifyChanged() => Changed?.Invoke();
}
