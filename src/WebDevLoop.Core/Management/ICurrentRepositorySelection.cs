namespace WebDevLoop.Core.Management;

/// <summary>The repository the UI is currently looking at. View context only: it never starts, stops or filters scheduling.</summary>
public interface ICurrentRepositorySelection
{
    int? CurrentRepositoryId { get; }

    void Select(int? repositoryId);
}
