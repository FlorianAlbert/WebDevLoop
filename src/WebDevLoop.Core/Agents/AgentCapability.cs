namespace WebDevLoop.Core.Agents;

public enum AgentCapability
{
    ReadFiles,
    WriteNotes,
    WriteFiles,
    RunShellCommands,
    CreateLocalCommit,
    UseBrowser,
    ReportResult,
    FetchRemote,
    PushRefs,
    ManageBranches,
    ManageWorktrees,
    CreatePullRequest,
    ManageIssues,
    ManageStacks,
}
