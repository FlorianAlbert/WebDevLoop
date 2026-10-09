using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>
/// The read-only checkout of the integration tip the parent-spec reviewers inspect: a run-scoped directory beside the
/// exploration notes (never inside the clone) on its own run-scoped branch, so the integration branch itself is never
/// checked out.
/// </summary>
public sealed class ParentReviewWorkspace
{
    private const string CheckoutDirectoryName = "parent-review";

    private ParentReviewWorkspace(string checkoutDirectory, BranchName branch)
    {
        CheckoutDirectory = checkoutDirectory;
        Branch = branch;
    }

    public string CheckoutDirectory { get; }

    public BranchName Branch { get; }

    public static ParentReviewWorkspace For(string workspaceRoot, RunId runId)
    {
        RunWorkspaceLayout layout = RunWorkspaceLayout.For(workspaceRoot, runId);
        return new ParentReviewWorkspace(Path.Combine(layout.RunDirectory, CheckoutDirectoryName), new BranchName($"webdevloop/{runId}/parent-review"));
    }
}
