namespace WebDevLoop.Core.Management;

public sealed class CurrentRepositorySelection : ICurrentRepositorySelection
{
    private int? _current;

    public int? CurrentRepositoryId => _current;

    public void Select(int? repositoryId) => _current = repositoryId;
}
