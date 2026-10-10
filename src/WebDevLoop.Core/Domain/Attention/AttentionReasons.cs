namespace WebDevLoop.Core.Domain;

/// <summary>
/// The catalogue of situations that lead to <c>NeedsAttention</c>, one factory per <see cref="AttentionCode"/> (some
/// codes have a ticket and a spec flavour). The factory is the only place that words the guidance, so every site that
/// parks work hands over the technical <c>details</c> it already had and gets plain-language guidance with it.
/// Wording rules: one sentence per summary and "why it matters", numbered steps that start with a verb, commands only
/// where the user really has to leave the app, and a consequence for every button.
/// </summary>
public static class AttentionReasons
{
    private const string GitHub = "https://github.com/";

    private static string Url(string repository) => GitHub + repository;

    // ----- Buttons ---------------------------------------------------------------------------------------------

    private static AttentionAction Retry(string consequence, string label = "Retry") => new(AttentionActionKind.Retry, label, consequence);

    private static readonly AttentionAction SkipTicket = new(
        AttentionActionKind.Skip,
        "Skip",
        "Gives up on this ticket without its change. Tickets that depend on it are released and continue without it.");

    private static readonly AttentionAction SkipTicketAndDependents = new(
        AttentionActionKind.SkipWithDependents,
        "Skip with dependents",
        "Gives up on this ticket and on every ticket that depends on it. The rest of the spec carries on.");

    private static readonly AttentionAction AbortTicket = new(
        AttentionActionKind.Abort,
        "Abort ticket",
        "Stops this ticket for good. Tickets that depend on it stay blocked, so the spec cannot finish unless you skip or recreate them.");

    private static readonly AttentionAction AbortRun = new(
        AttentionActionKind.Abort,
        "Abort run",
        "Cancels the whole run and all of its unfinished tickets. Anything already pushed to GitHub stays there; nothing is deleted.");

    private static AttentionAction[] TicketButtons(string retry, string retryLabel = "Retry") =>
        [Retry(retry, retryLabel), SkipTicket, SkipTicketAndDependents, AbortTicket];

    private static AttentionAction[] RunButtons(string retry, string retryLabel = "Retry") => [Retry(retry, retryLabel), AbortRun];

    private static AttentionAction[] Buttons(bool forTicket, string retry, string retryLabel = "Retry") =>
        forTicket ? TicketButtons(retry, retryLabel) : RunButtons(retry, retryLabel);

    private static AttentionStep Step(string text, string? command = null) => new(text, command);

    private static AttentionStep SettingsStep(string text, string section) => new(text, null, "Open Settings", "/settings" + section);

    private static AttentionStep GitHubStep(string text, string label, string href) => new(text, null, label, href);

    private static AttentionReason Build(
        AttentionCode code,
        AttentionCause cause,
        string summary,
        string why,
        string details,
        IEnumerable<AttentionAction> actions,
        IEnumerable<AttentionStep>? steps = null,
        string? autoFix = null) =>
        new(code, summary, why, details, cause, autoFix is null ? null : new AttentionAutoFix(autoFix), null, steps?.ToArray(), actions.ToArray());

    // ----- Preparation -------------------------------------------------------------------------------------------

    public static AttentionReason RepositoryNotRegistered(int repositoryId) => Build(
        AttentionCode.RepositoryNotRegistered,
        AttentionCause.You,
        "The repository of this run is no longer registered in WebDevLoop.",
        "Without its repository record WebDevLoop has no clone to work in and nowhere to push to.",
        $"Repository {repositoryId} is no longer registered.",
        [Retry("WebDevLoop looks for the repository record again. This only helps once the repository is registered under the same entry.", "Retry"), AbortRun],
        [
            new AttentionStep("Add the repository again on the Repositories page.", null, "Open Repositories", "/repositories"),
            Step("Queue the spec again from the Queue page; this run cannot be moved to the new entry."),
            Step("Abort this run when you no longer need it."),
        ]);

    public static AttentionReason SpecHasNoTickets(string details) => Build(
        AttentionCode.SpecHasNoTickets,
        AttentionCause.You,
        "The spec has no open tickets to implement.",
        "WebDevLoop does not split a spec itself; a run without tickets would only review an empty change.",
        details,
        RunButtons("WebDevLoop reads the sub-issues of the spec again. Nothing is lost."),
        [
            Step("Open the spec issue on GitHub and add the work as sub-issues (tickets) of the spec."),
            Step("Come back to this page and press Retry."),
        ]);

    public static AttentionReason TicketDependencyCycle(string details) => Build(
        AttentionCode.TicketDependencyCycle,
        AttentionCause.You,
        "The tickets of this spec block each other in a circle, so none of them could ever start.",
        "WebDevLoop needs an order to work the tickets in.",
        details,
        RunButtons("WebDevLoop reads the ticket dependencies again. Nothing is lost."),
        [
            Step("On GitHub, open the tickets named in the technical details and remove one of the 'blocked by' links that closes the circle."),
            Step("Come back to this page and press Retry."),
        ]);

    public static AttentionReason BaseBranchMissing(string branch, string repository, string details) => Build(
        AttentionCode.BaseBranchMissing,
        AttentionCause.You,
        $"The base branch '{branch}' does not exist on GitHub.",
        "WebDevLoop starts every run from the base branch, so it cannot begin without it.",
        details,
        RunButtons($"WebDevLoop looks for '{branch}' on GitHub again and starts preparing the run."),
        [
            GitHubStep($"Check which branches {repository} has.", "Open the repository on GitHub", Url(repository) + "/branches"),
            SettingsStep("If the branch has a different name, set it as the base branch in Settings.", "#section-general"),
            Step("Press Retry."),
        ]);

    public static AttentionReason IntegrationBranchExists(string branch, string details, string? clonePath = null) => Build(
        AttentionCode.IntegrationBranchExists,
        AttentionCause.WebDevLoop,
        "A leftover integration branch of an earlier attempt of this run is in the way.",
        "WebDevLoop must create the run's integration branch at a known starting point before it can build on it.",
        details,
        RunButtons("WebDevLoop prepares the run again. Nothing outside this run's own branch is touched."),
        [
            Step("Delete the leftover branch in the local clone of the repository, then press Retry.", clonePath is null ? $"git branch -D {branch}" : $"git -C {clonePath} branch -D {branch}"),
        ],
        autoFix: "Reset the run's own integration branch to the run's starting point (only when no ticket has been integrated yet).");

    public static AttentionReason ExplorationFailed(string details) => Build(
        AttentionCode.ExplorationFailed,
        AttentionCause.WebDevLoop,
        "The explorer agent could not finish its look at the repository.",
        "Its notes help the other agents, but the run does not continue until the exploration step has ended cleanly.",
        details,
        RunButtons("WebDevLoop starts the preparation again, including a new exploration. Nothing is lost."),
        [
            Step("Open the exploration step on this page and read the agent's last message to see where it stopped."),
            SettingsStep("If it ran out of time, raise the timeout of the Explorer role or pick a different model.", "#section-roles"),
            Step("Press Retry."),
        ],
        autoFix: "Run the exploration once more with the same input.");

    // ----- Implementation ----------------------------------------------------------------------------------------

    public static AttentionReason ImplementationFailed(int attempts, string lastFailure, bool fix = false) => Build(
        fix ? AttentionCode.FixFailed : AttentionCode.ImplementationFailed,
        AttentionCause.WebDevLoop,
        fix ? "The implementer agent could not apply the review fixes." : "The implementer agent could not complete this ticket.",
        "The change for this ticket does not exist yet, so nothing can be reviewed or integrated.",
        $"{(fix ? "Fixing the review findings" : "Implementation")} failed after {attempts} attempt(s): {lastFailure}",
        TicketButtons(fix
            ? "WebDevLoop starts a new fix session for the same review findings. Work already integrated is kept."
            : "WebDevLoop starts the implementation of this ticket again from the current integration branch. Work already integrated is kept."),
        [
            Step("Open the failed step on this page and read the agent log to see where it got stuck."),
            Step("If the ticket text is unclear, edit the issue on GitHub so the task is precise, then press Retry."),
            SettingsStep("If the agent keeps running out of time or tools, change the model or timeout of the Implementer role.", "#section-roles"),
        ]).WithTried($"Ran the implementer agent {attempts} time(s), each in a fresh session.");

    public static AttentionReason ImplementerBlocked(string agentMessage, bool fix = false) => Build(
        AttentionCode.ImplementerBlocked,
        AttentionCause.You,
        "The implementer agent says it cannot continue without something it does not have.",
        "WebDevLoop cannot guess what is missing; retrying without a change would end the same way.",
        $"Implementer reported blocked: {agentMessage}",
        TicketButtons("WebDevLoop starts the implementation again with the ticket as it is now. Work already integrated is kept."),
        [
            Step($"Read what the agent asks for: \"{agentMessage}\""),
            Step("Add the missing detail to the ticket on GitHub (or remove the blocker, e.g. an unavailable tool or credential)."),
            Step("Press Retry."),
        ]);

    public static AttentionReason PromptNotRenderable(string role, string details, bool forTicket = true) => Build(
        AttentionCode.PromptNotRenderable,
        AttentionCause.You,
        $"The prompt template of the {role} role cannot be filled in.",
        "Without a prompt WebDevLoop cannot start the agent.",
        details,
        Buttons(forTicket, "WebDevLoop renders the prompt again with your current settings and starts the agent."),
        [
            SettingsStep($"Open the {role} role and fix the placeholder named in the technical details (or reset the template to the default).", "#section-roles"),
            Step("Press Retry."),
        ]);

    public static AttentionReason WorktreeNotClean(string worktreePath, string branch, string details) => Build(
        AttentionCode.WorktreeNotClean,
        AttentionCause.WebDevLoop,
        "The ticket's working folder contains files that were not committed.",
        "WebDevLoop cannot safely continue because it might lose or mix up changes.",
        details,
        TicketButtons("WebDevLoop cleans the working folder again and re-checks it, then continues with the reviewed commit. Nothing committed is lost."),
        [
            Step("Look at what is in the folder:", $"git -C {worktreePath} status"),
            Step($"If it only holds build or cache files, remove them:", $"git -C {worktreePath} clean -fdx"),
            Step($"If tracked files were changed by hand, discard that change (a patch of earlier cleanups is kept in the run folder):", $"git -C {worktreePath} reset --hard {branch}"),
            Step("Press Retry."),
        ],
        autoFix: "Save tracked changes as a patch in the run folder, reset the ticket worktree and remove untracked and ignored files inside it.");

    public static AttentionReason TicketBranchNotBasedOnIntegration(string branch, string details) => Build(
        AttentionCode.TicketBranchNotBasedOnIntegration,
        AttentionCause.WebDevLoop,
        "The ticket's branch does not contain the latest state of the integration branch.",
        "Reviewing or integrating it would show or apply changes that belong to other tickets.",
        details,
        TicketButtons("WebDevLoop brings the ticket branch up to date with the integration branch and continues. Your ticket's own changes stay."),
        [
            Step($"Open the worktree of '{branch}' and merge the integration branch into it, then press Retry (details have the exact commits)."),
        ],
        autoFix: "Merge the integration branch into the ticket branch; ask only when the merge conflicts.");

    public static AttentionReason ReportedCommitMismatch(string details) => Build(
        AttentionCode.ReportedCommitMismatch,
        AttentionCause.WebDevLoop,
        "The implementer reported a commit that is not what the ticket branch actually points to.",
        "WebDevLoop only reviews what it has checked itself, so it will not trust the report.",
        details,
        TicketButtons("WebDevLoop runs the implementation again from the current integration branch."),
        [
            Step("Open the implementer step and compare the reported commit with the branch shown in the technical details."),
            Step("Press Retry; if it happens again, change the model of the Implementer role."),
        ]);

    // ----- Review ------------------------------------------------------------------------------------------------

    public static AttentionReason ReviewFailed(string details) => Build(
        AttentionCode.ReviewFailed,
        AttentionCause.WebDevLoop,
        "A reviewer agent could not finish its review of this ticket.",
        "The ticket cannot be integrated before both reviews have a result.",
        details,
        TicketButtons("WebDevLoop runs a fresh review of the same commit with a full set of review rounds. Nothing is lost."),
        [
            Step("Open the failed review step on this page and read the agent's last message."),
            SettingsStep("If the reviewer ran out of time, raise the timeout of the reviewer roles.", "#section-roles"),
            Step("Press Retry."),
        ]);

    public static AttentionReason ReviewIterationsExhausted(int findings, int rounds, int limit) => Build(
        AttentionCode.ReviewIterationsExhausted,
        AttentionCause.Decision,
        $"The reviewers still find {findings} issue(s) after {rounds} round(s) of fixes.",
        "WebDevLoop stops at the review limit instead of fixing and reviewing forever.",
        $"Review still found {findings} issue(s) after {rounds} review round(s); the limit is {limit}.",
        TicketButtons("WebDevLoop starts a new review round on the current commit with a full round budget, so the fixer gets another try."),
        [
            Step("Open the latest review step and read the open findings."),
            Step("If they are acceptable, merge the ticket yourself later and press Skip (its change stays on its branch)."),
            SettingsStep("To give the fixer more rounds from now on, raise the review iteration limit in Settings, then press Retry.", "#section-limits"),
        ]);

    public static AttentionReason FixFailed(int attempts, string lastFailure) => ImplementationFailed(attempts, lastFailure, fix: true);

    // ----- Integration -------------------------------------------------------------------------------------------

    public static AttentionReason IntegrationTemporaryFailure(int failures, string checkpoint, string exceptionMessage) => Build(
        AttentionCode.IntegrationTemporaryFailure,
        AttentionCause.WebDevLoop,
        "GitHub or the network did not respond while WebDevLoop was publishing this ticket.",
        "The change is ready; only the last step of putting it on GitHub failed, so no work is lost.",
        $"Integration failed {failures} time(s) in a row at checkpoint {checkpoint}: {exceptionMessage}",
        TicketButtons("WebDevLoop continues publishing from the last completed step. Nothing is duplicated."),
        [
            GitHubStep("Check whether GitHub has an incident.", "GitHub status", "https://www.githubstatus.com"),
            Step("When your connection and GitHub work again, press Retry."),
        ],
        autoFix: "Retry the failed step with growing pauses between attempts.");

    public static AttentionReason IntegrationFailed(int failures, string checkpoint, string exceptionMessage) => Build(
        AttentionCode.IntegrationFailed,
        AttentionCause.WebDevLoop,
        "WebDevLoop failed to publish this ticket's change.",
        "The ticket is reviewed and ready, but its change is not on the integration branch or in a pull request yet.",
        $"Integration failed {failures} time(s) in a row at checkpoint {checkpoint}: {exceptionMessage}",
        TicketButtons("WebDevLoop continues publishing from the last completed step. Nothing is duplicated."),
        [
            Step("Read the technical details for the error; GitHub permission and branch protection messages are the usual cause."),
            Step("Fix what the message names, then press Retry."),
        ]);

    public static AttentionReason TicketHasNoChanges(string branch, string source, string tip) => Build(
        AttentionCode.TicketHasNoChanges,
        AttentionCause.WebDevLoop,
        "The reviewed ticket branch adds nothing that is not on the integration branch yet.",
        "There is no change to put into a pull request.",
        $"Ticket branch '{branch}' at {source} has no changes relative to the integration tip {tip}.",
        TicketButtons("WebDevLoop implements the ticket again from the current integration branch."),
        [
            Step("Check whether another ticket already delivered this work; if so, press Skip."),
            Step("Otherwise press Retry so the agent produces the change."),
        ],
        autoFix: "Mark the ticket as skipped (\"no changes needed\"): both reviewers approved a branch that adds nothing to the integration branch.");

    public static AttentionReason MergeConflictUnresolved(string details, string? worktreePath = null) => Build(
        AttentionCode.MergeConflictUnresolved,
        AttentionCause.WebDevLoop,
        "This ticket's change conflicts with other tickets' changes and the conflict-resolver agent could not settle it.",
        "Two tickets changed the same lines, and the combined result has to be chosen by someone.",
        details,
        TicketButtons("WebDevLoop runs the conflict resolution again from the current integration branch."),
        [
            Step("Open the conflict-resolver step on this page to see which files conflict and what the agent tried."),
            worktreePath is null
                ? Step("Resolve the conflict in the ticket's worktree (merge the integration branch into the ticket branch, fix the files, commit).")
                : Step("Resolve the conflict in the ticket's worktree: merge the integration branch, fix the files, commit.", $"git -C {worktreePath} status"),
            Step("Press Retry."),
        ]).WithTried("Ran the conflict-resolver agent on the conflicting files.");

    public static AttentionReason IntegrationBranchMoved(string branch, string details, bool forTicket = true) => Build(
        AttentionCode.IntegrationBranchMoved,
        AttentionCause.You,
        "The integration branch was changed outside WebDevLoop.",
        "WebDevLoop only builds on the branch state it created, otherwise it could overwrite someone else's work.",
        details,
        Buttons(forTicket, "WebDevLoop checks the branch again and continues if it is back at the expected commit."),
        [
            Step($"Find out who pushed to '{branch}':", $"git log --oneline origin/{branch} -5"),
            Step($"If the extra commit should be dropped, reset the branch on GitHub to the last commit WebDevLoop created (shown in the details)."),
            Step("If the extra commit should stay, abort this run and queue the spec again so it starts from the new state."),
        ]);

    public static AttentionReason IntegrationPushRejected(string branch, string details, bool forTicket = true) => Build(
        AttentionCode.IntegrationPushRejected,
        AttentionCause.You,
        $"GitHub refused the push of the branch '{branch}'.",
        "The remote branch is not where WebDevLoop expects it, so pushing would overwrite someone else's commits.",
        details,
        Buttons(forTicket, "WebDevLoop tries the push again. It succeeds once the remote branch is back at the expected commit."),
        [
            Step("Check who changed the branch on GitHub:", $"git log --oneline origin/{branch} -5"),
            Step("Remove the unexpected commits from the remote branch, or abort this run and queue the spec again."),
            Step("Press Retry."),
        ]);

    public static AttentionReason StackBranchExists(string branch, string details) => Build(
        AttentionCode.StackBranchExists,
        AttentionCause.You,
        $"A branch named '{branch}' already exists on GitHub with different content.",
        "WebDevLoop creates this branch for the ticket's pull request and will not overwrite a branch it did not create.",
        details,
        TicketButtons("WebDevLoop pushes the ticket's stack branch again. It succeeds once the old branch is gone."),
        [
            Step("Make sure nobody needs the old branch, then delete it:", $"git push origin --delete {branch}"),
            Step("Press Retry."),
        ]);

    public static AttentionReason PullRequestNotOpen(int number, string state, string details, string repository) => Build(
        AttentionCode.PullRequestNotOpen,
        AttentionCause.Decision,
        $"Pull request #{number} of this ticket was {state.ToLowerInvariant()} outside WebDevLoop.",
        "The stack of pull requests no longer matches what WebDevLoop built.",
        details,
        TicketButtons($"WebDevLoop looks at pull request #{number} again and continues if it is open."),
        [
            GitHubStep("Look at the pull request to see what happened.", $"Open #{number}", $"{Url(repository)}/pull/{number}"),
            Step($"If it was closed by mistake, reopen it:", $"gh pr reopen {number} -R {repository}"),
            Step("Otherwise press Skip, or abort the run and queue the spec again."),
        ]);

    public static AttentionReason PullRequestStackChanged(string details, string repository) => Build(
        AttentionCode.PullRequestStackChanged,
        AttentionCause.You,
        "The pull request stack on GitHub was changed by hand and no longer matches the ticket order.",
        "Each pull request has to sit directly on top of the one below, otherwise reviewers see the wrong changes.",
        details,
        TicketButtons("WebDevLoop checks the stack again and continues when the order is right."),
        [
            GitHubStep("Open the pull requests of this run and put them back in the order shown in the technical details.", "Open pull requests", Url(repository) + "/pulls"),
            Step("Press Retry."),
        ]);

    public static AttentionReason DiffVerificationFailed(int number, string problem, string repository) => Build(
        AttentionCode.DiffVerificationFailed,
        AttentionCause.You,
        $"Pull request #{number} does not show exactly this ticket's change.",
        "A pull request with the wrong content would mislead reviewers.",
        $"Diff verification of pull request #{number} failed: {problem}",
        TicketButtons($"WebDevLoop compares pull request #{number} with the ticket's change again."),
        [
            GitHubStep("Open the pull request and compare its files with the ticket.", $"Open #{number}", $"{Url(repository)}/pull/{number}/files"),
            Step("If the pull request was edited or retargeted by hand, undo that, then press Retry."),
        ]);

    public static AttentionReason ForeignPullRequest(int number, string branch, string details, string repository) => Build(
        AttentionCode.ForeignPullRequest,
        AttentionCause.You,
        $"Pull request #{number} already uses the branch '{branch}' but was not created by this run.",
        "WebDevLoop does not take over pull requests it did not open.",
        details,
        TicketButtons("WebDevLoop looks for the pull request again."),
        [
            GitHubStep("Open the pull request and close it if it is not needed.", $"Open #{number}", $"{Url(repository)}/pull/{number}"),
            Step("Press Retry."),
        ]);

    // ----- Recovery ----------------------------------------------------------------------------------------------

    public static AttentionReason InterruptedRepeatedly(string stepKind, int interruptions, int maxRetries, bool forTicket) => Build(
        AttentionCode.InterruptedRepeatedly,
        AttentionCause.WebDevLoop,
        $"WebDevLoop was interrupted again and again while working on the {stepKind.ToLowerInvariant()} step.",
        "Restarting the same work forever would not help, so it stopped after the allowed number of restarts.",
        $"The {stepKind} step was interrupted {interruptions} times in a row (restarts or lost runners); at most {maxRetries} retries are allowed.",
        Buttons(forTicket, "WebDevLoop starts the step again with a fresh restart budget."),
        [
            Step("Make sure the app stays running (no restarts, enough memory and disk), then press Retry."),
        ]).WithTried($"Restarted the step automatically {maxRetries} time(s) after each interruption.");

    // ----- Parent review, testing, completion --------------------------------------------------------------------

    public static AttentionReason ParentReviewFailed(string details) => Build(
        AttentionCode.ParentReviewFailed,
        AttentionCause.WebDevLoop,
        "The final review of the whole spec could not be completed.",
        "The run cannot move on to testing without a review result for every part of the review.",
        details,
        RunButtons("WebDevLoop runs the final review again on the current integration branch."),
        [
            Step("Open the failed review step on this page and read the agent's last message."),
            Step("Press Retry."),
        ]);

    public static AttentionReason ParentReviewCycleLimit(int findings, int cycles, int limit, string details) => Build(
        AttentionCode.ParentReviewCycleLimit,
        AttentionCause.Decision,
        $"The final review still finds {findings} issue(s) after {cycles} cycle(s).",
        "WebDevLoop stops at the cycle limit instead of creating new tickets forever.",
        details,
        RunButtons("WebDevLoop works the open tickets and then reviews again with a fresh cycle budget."),
        [
            Step("Open the latest final review and the new finding tickets and decide whether the remaining findings matter."),
            SettingsStep("To allow more cycles, raise the parent review cycle limit in Settings, then press Retry.", "#section-limits"),
            Step("To accept the result as it is, close the finding tickets on GitHub and press Retry."),
        ]);

    public static AttentionReason NoNewWork(string phase, string details) => Build(
        AttentionCode.NoNewWork,
        AttentionCause.Decision,
        $"The {phase} only repeated findings whose tickets are already done.",
        "Working the same tickets again would not change anything.",
        details,
        RunButtons($"WebDevLoop runs the {phase} again on the current integration branch."),
        [
            Step("Open the listed finding tickets and check whether the problem was really fixed."),
            Step("If it is fixed or acceptable, press Retry; otherwise edit the ticket on GitHub so it describes what is still wrong."),
        ]);

    public static AttentionReason TesterBlocked(string agentMessage) => Build(
        AttentionCode.TesterBlocked,
        AttentionCause.You,
        "The tester agent could not test the application.",
        "The run cannot be marked ready without a test result.",
        $"The tester could not test the application: {agentMessage}",
        RunButtons("WebDevLoop starts the testing again on the current integration branch."),
        [
            Step($"Read what the tester said: \"{agentMessage}\""),
            Step("Fix what it names (for example the start command or a missing dependency in the repository)."),
            Step("Press Retry."),
        ]);

    public static AttentionReason TestingFailed(string details) => Build(
        AttentionCode.TestingFailed,
        AttentionCause.WebDevLoop,
        "The testing of the spec could not be completed.",
        "The run cannot be marked ready without a test result.",
        details,
        RunButtons("WebDevLoop starts the testing again on the current integration branch."),
        [
            Step("Open the tester step on this page and read the log to see why it stopped."),
            Step("Press Retry."),
        ]);

    public static AttentionReason TestCycleLimit(int cycles, string details) => Build(
        AttentionCode.TestCycleLimit,
        AttentionCause.Decision,
        $"The tester still finds problems after {cycles} test cycle(s).",
        "WebDevLoop stops at the cycle limit instead of creating new tickets forever.",
        details,
        RunButtons("WebDevLoop works the open tickets and then tests again with a fresh cycle budget."),
        [
            Step("Open the latest test report and the finding tickets and decide whether the remaining problems matter."),
            SettingsStep("To allow more cycles, raise the tester cycle limit in Settings, then press Retry.", "#section-limits"),
        ]);

    public static AttentionReason IntegrationTipNotTested(string details) => Build(
        AttentionCode.IntegrationTipNotTested,
        AttentionCause.WebDevLoop,
        "The code on the integration branch is not the code the tester passed.",
        "Marking pull requests ready would publish code that was never tested.",
        details,
        RunButtons("WebDevLoop tests the current integration branch again."),
        [Step("Press Retry.")]);

    public static AttentionReason StackVerificationFailed(string problem, string repository) => Build(
        AttentionCode.StackVerificationFailed,
        AttentionCause.You,
        "The pull requests on GitHub do not match what WebDevLoop built.",
        "WebDevLoop will not mark pull requests ready for review while they might show the wrong changes.",
        $"Stack verification failed before marking the PRs ready: {problem}",
        RunButtons("WebDevLoop checks the pull requests again and marks them ready if they match."),
        [
            GitHubStep("Open the pull requests of this run and compare them with the technical details.", "Open pull requests", Url(repository) + "/pulls"),
            Step("Undo manual changes to the pull requests (title, base branch, commits), then press Retry."),
        ]);

    public static AttentionReason PullRequestsClosedUnmerged(string stack, string trunk, string repository) => Build(
        AttentionCode.PullRequestsClosedUnmerged,
        AttentionCause.Decision,
        "The pull requests of this run were closed without being merged.",
        "The work is finished but did not reach the base branch.",
        $"The PR stack {stack} was closed without being merged into '{trunk}'.",
        RunButtons("WebDevLoop looks at the pull requests again and carries on if they are open or merged."),
        [
            GitHubStep("Open the pull requests to see who closed them and why.", "Open pull requests", Url(repository) + "/pulls?q=is%3Apr+is%3Aclosed"),
            Step("To keep going, reopen them and press Retry; otherwise abort the run."),
        ]);

    public static AttentionReason TrunkMissingStack(string details, string trunk) => Build(
        AttentionCode.TrunkMissingStack,
        AttentionCause.You,
        $"All pull requests are merged, but '{trunk}' does not contain the final change yet.",
        "WebDevLoop cannot confirm that the work arrived on the base branch.",
        details,
        RunButtons("WebDevLoop checks the base branch again."),
        [
            Step("Check on GitHub that the last pull request was merged into the base branch and not into another branch."),
            Step("Press Retry."),
        ]);

    // ----- Anything ----------------------------------------------------------------------------------------------

    public static AttentionReason InternalInconsistency(string details, bool forTicket) => Build(
        AttentionCode.InternalInconsistency,
        AttentionCause.WebDevLoop,
        "WebDevLoop found its own records in an unexpected state.",
        "This is a bug in WebDevLoop, not something you did; continuing could produce wrong results.",
        details,
        Buttons(forTicket, "WebDevLoop tries the step again. It only helps if the unexpected state was temporary."),
        [
            Step("Copy the technical details below and report them as an issue of WebDevLoop."),
            Step("Abort the run if Retry does not help."),
        ]);

    /// <summary>Renders rows stored before reasons were structured.</summary>
    public static AttentionReason Unclassified(string? details, bool forTicket) => Build(
        AttentionCode.Unclassified,
        AttentionCause.WebDevLoop,
        "WebDevLoop stopped and needs your decision on how to continue.",
        "This stop was recorded before WebDevLoop could explain its causes.",
        details ?? "(no reason recorded)",
        Buttons(forTicket, "WebDevLoop resumes the phase that stopped."),
        [Step("Read the technical details below, fix what they name, then press Retry.")]);
}
