namespace WebDevLoop.Infrastructure.Git;

public sealed class WorkspacePathOutsideRootException(string path, string workspaceRoot)
    : Exception($"Path '{path}' is outside the workspace root '{workspaceRoot}'.")
{
    public string Path { get; } = path;

    public string WorkspaceRoot { get; } = workspaceRoot;
}
