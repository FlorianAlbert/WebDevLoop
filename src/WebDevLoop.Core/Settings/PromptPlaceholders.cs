using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>
/// The closed set of prompt template placeholders. A placeholder is written as
/// <c>{name}</c> with a lower-case snake_case name; any other brace usage is literal text.
/// </summary>
public static class PromptPlaceholders
{
    public const string RepoOwner = "repo_owner";
    public const string RepoName = "repo_name";
    public const string RepoUrl = "repo_url";
    public const string BaseBranch = "base_branch";
    public const string IntegrationBranch = "integration_branch";
    public const string IntegrationTipSha = "integration_tip_sha";
    public const string WorkspaceRoot = "workspace_root";
    public const string SkillsRoot = "skills_root";
    public const string RunId = "run_id";
    public const string Attempt = "attempt";
    public const string ParentSpecIssueNumber = "parent_spec_issue_number";
    public const string ParentSpecTitle = "parent_spec_title";
    public const string ParentSpecBody = "parent_spec_body";
    public const string SpecTickets = "spec_tickets";
    public const string TicketIssueNumber = "ticket_issue_number";
    public const string TicketTitle = "ticket_title";
    public const string TicketBody = "ticket_body";
    public const string TicketDependencies = "ticket_dependencies";
    public const string WorktreePath = "worktree_path";
    public const string BranchName = "branch_name";
    public const string DiffBaseRef = "diff_base_ref";
    public const string DiffHeadRef = "diff_head_ref";
    public const string ChangedFiles = "changed_files";
    public const string ConflictingFiles = "conflicting_files";
    public const string ReviewAxis = "review_axis";
    public const string ReviewScope = "review_scope";
    public const string ReviewFindingsJson = "review_findings_json";
    public const string ReviewIteration = "review_iteration";
    public const string MaxReviewIterations = "max_review_iterations";
    public const string TesterInstructions = "tester_instructions";
    public const string ReservedPort = "reserved_port";
    public const string AppUrl = "app_url";
    public const string ExplorationNotesPath = "exploration_notes_path";
    public const string AttentionCode = "attention_code";
    public const string AttentionSummary = "attention_summary";
    public const string AttentionDetails = "attention_details";
    public const string FailedPhase = "failed_phase";
    public const string RemediationTried = "remediation_tried";
    public const string GitState = "git_state";
    public const string GitHubState = "github_state";
    public const string RecentAgentLogs = "recent_agent_logs";
    public const string TroubleshootingContextPath = "troubleshooting_context_path";
    public const string BackupPath = "backup_path";
    public const string IntegrationWorktreePath = "integration_worktree_path";

    /// <summary>Every known placeholder name with a human-readable description (for settings UI/help).</summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } = new Dictionary<string, string>
    {
        [RepoOwner] = "GitHub owner (user or organisation) of the repository.",
        [RepoName] = "GitHub repository name.",
        [RepoUrl] = "HTML URL of the GitHub repository.",
        [BaseBranch] = "Trunk/base branch the spec's PR stack targets.",
        [IntegrationBranch] = "Run-scoped local integration branch (staging branch for review and testing).",
        [IntegrationTipSha] = "Commit SHA of the integration branch tip when the step started.",
        [WorkspaceRoot] = "Root directory of the server-side workspace for this repository.",
        [SkillsRoot] = "Directory containing the bundled agent skills.",
        [RunId] = "Identifier of the spec run.",
        [Attempt] = "1-based attempt number of the current step.",
        [ParentSpecIssueNumber] = "Issue number of the parent spec.",
        [ParentSpecTitle] = "Title of the parent spec issue.",
        [ParentSpecBody] = "Body snapshot of the parent spec issue.",
        [SpecTickets] = "Markdown list of the spec's tickets with numbers, titles, states and blockers.",
        [TicketIssueNumber] = "Issue number of the ticket.",
        [TicketTitle] = "Title of the ticket issue.",
        [TicketBody] = "Body snapshot of the ticket issue, including acceptance criteria.",
        [TicketDependencies] = "Tickets that block this ticket, already integrated into the integration branch.",
        [WorktreePath] = "Working directory assigned to the agent (ticket worktree, read-only checkout or test workspace).",
        [BranchName] = "Branch checked out in the assigned working directory.",
        [DiffBaseRef] = "Fixed point to diff against (merge-base side of a three-dot diff).",
        [DiffHeadRef] = "Ref or SHA whose changes are under review.",
        [ChangedFiles] = "Newline-separated list of files changed between the diff refs.",
        [ConflictingFiles] = "Newline-separated list of files that conflict when squash-merging onto the integration tip.",
        [ReviewAxis] = "Review axis as named by the report tool: 'coding_standards' or 'specification'.",
        [ReviewScope] = "Review scope: 'ticket' (single ticket branch) or 'parent_spec' (final parent-spec review of the integration branch).",
        [ReviewFindingsJson] = "JSON array of open review findings to fix; '[]' on the first implementation turn.",
        [ReviewIteration] = "0-based number of completed review rounds for this ticket.",
        [MaxReviewIterations] = "Maximum number of review rounds before the ticket needs attention.",
        [TesterInstructions] = "Repository-specific instructions for building, starting and stopping the application.",
        [ReservedPort] = "TCP port reserved by WebDevLoop for this test run.",
        [AppUrl] = "Base URL at which the application under test must be served.",
        [ExplorationNotesPath] = "Directory outside the repository holding exploration notes shared with later agents.",
        [AttentionCode] = "Reason code of the problem the troubleshooter looks at (e.g. WorktreeNotClean).",
        [AttentionSummary] = "Plain-language summary of the problem.",
        [AttentionDetails] = "Technical details of the failure: the exception text, paths and commit ids.",
        [FailedPhase] = "The phase of the run or ticket that failed and that WebDevLoop resumes once the problem is solved.",
        [RemediationTried] = "What WebDevLoop and earlier troubleshooter attempts already tried, one item per line.",
        [GitState] = "Git status, recent commits and branch tips of the ticket worktree and the integration branch.",
        [GitHubState] = "What WebDevLoop knows about the pull requests and the remote branches of the ticket (read-only).",
        [RecentAgentLogs] = "The tail of the log of the last agent step of the ticket or run.",
        [TroubleshootingContextPath] = "Read-only directory with the full context of the problem and the backups taken before the session.",
        [BackupPath] = "Directory holding the patch of the uncommitted changes WebDevLoop saved before the session started.",
        [IntegrationWorktreePath] = "Scratch checkout of the integration branch tip on its own branch, safe to inspect and modify.",
    };

    private static readonly string[] SpecContext =
    [
        RepoOwner, RepoName, RepoUrl, BaseBranch, IntegrationBranch, IntegrationTipSha, WorkspaceRoot, SkillsRoot,
        RunId, Attempt, ParentSpecIssueNumber, ParentSpecTitle, ParentSpecBody, SpecTickets, ExplorationNotesPath,
    ];

    private static readonly string[] TicketContext = [TicketIssueNumber, TicketTitle, TicketBody, TicketDependencies];

    private static readonly string[] WorkspaceContext = [WorktreePath, BranchName];

    private static readonly string[] ReviewContext =
    [
        ReviewAxis, ReviewScope, ReviewIteration, MaxReviewIterations, DiffBaseRef, DiffHeadRef, ChangedFiles,
    ];

    private static readonly IReadOnlyDictionary<AgentRole, IReadOnlySet<string>> ByRole =
        new Dictionary<AgentRole, IReadOnlySet<string>>
        {
            [AgentRole.Explorer] = Set(SpecContext, [WorktreePath]),
            [AgentRole.Implementer] = Set(SpecContext, TicketContext, WorkspaceContext,
                [ReviewFindingsJson, ReviewIteration, MaxReviewIterations]),
            [AgentRole.ReviewerCodingStandards] = Set(SpecContext, TicketContext, WorkspaceContext, ReviewContext),
            [AgentRole.ReviewerSpecification] = Set(SpecContext, TicketContext, WorkspaceContext, ReviewContext),
            [AgentRole.ConflictResolver] = Set(SpecContext, TicketContext, WorkspaceContext,
                [ConflictingFiles, ChangedFiles]),
            [AgentRole.Tester] = Set(SpecContext, WorkspaceContext, [TesterInstructions, ReservedPort, AppUrl]),
            [AgentRole.Troubleshooter] = Set(SpecContext, TicketContext, WorkspaceContext,
            [
                AttentionCode, AttentionSummary, AttentionDetails, FailedPhase, RemediationTried, GitState, GitHubState,
                RecentAgentLogs, TroubleshootingContextPath, BackupPath, IntegrationWorktreePath,
            ]),
        };

    public static IReadOnlyCollection<string> All { get; } = Descriptions.Keys.ToArray();

    public static bool IsKnown(string name) => Descriptions.ContainsKey(name);

    /// <summary>Placeholders the app supplies when rendering a prompt for <paramref name="role"/>.</summary>
    public static IReadOnlySet<string> AvailableFor(AgentRole role) =>
        ByRole.TryGetValue(role, out IReadOnlySet<string>? names)
            ? names
            : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown agent role.");

    private static IReadOnlySet<string> Set(params string[][] groups) =>
        groups.SelectMany(group => group).ToHashSet(StringComparer.Ordinal);
}
