# FAQ and troubleshooting

## Setup and sign-in

**Why can't I queue a spec? The form is disabled.**
WebDevLoop is in diagnostic-only mode. Open [Health](health.md), fix the failed checks and click **Re-check**. The most common cause is that you are not signed in to GitHub. See [GitHub](github.md).

**The browser warns about the certificate.**
The app uses the .NET development certificate. Trust it once with `dotnet dev-certs https --trust`.

**GitHub shows a callback or redirect URL error when I sign in.**
The callback URL of your GitHub App must match the address you opened. For `https://localhost:7233` it is `https://localhost:7233/auth/github/callback`. Add one callback URL for every address you use.

**My repository is not in the "Add repository" list.**
The GitHub App is not installed on it. On the [GitHub](github.md) page click **Manage repository access**, add the repository, then **Refresh**.

**A registered repository shows "Not accessible".**
The same cause. Add it to the App installation. See [GitHub](github.md#registered-repositories).

**Do I need to sign in again after a restart?**
No. The sign-in is stored encrypted on your machine and renews itself. You only sign in again after a long pause, after signing out, or if you revoked the App on GitHub.

## Specs and queue

**I queued a spec and it says "The spec has no open tickets to implement".**
WebDevLoop does not split specs. Add the work as open sub-issues of the spec on GitHub, then press **Retry** on the run. See [Needs attention](needs-attention.md#the-spec-or-the-repository).

**My spec is "Queued" and nothing happens.**
It waits for a free slot. By default one spec per repository runs at a time. Check **Active slots** on the [Queue](queue.md#slots-and-mode) page. Raise **Max active specs per repository** in [Settings](settings.md#concurrency-review-and-retries) if you want more at once.

**My spec is "Waiting for dependency".**
The spec issue is blocked by another issue on GitHub. In **Wait for merge** mode it starts after the blocker's pull requests were merged. The row names the blockers. See [Spec runs](runs.md#blocked-by-specs).

**A ticket stays "Blocked".**
It waits for the tickets it depends on. Only integrated or skipped tickets unblock it. Look at **Blocked by** on the [ticket page](tickets-and-steps.md#header-and-details). If the blocker is stuck, fix it or skip it.

**Can I add or change tickets while a run is going?**
The tickets are read when the run starts. Changes to the spec's sub-issues later do not change a running run. To start over with new tickets, abort the run and queue the spec again.

**Why did a spec go back to "Running" after the tester?**
The tester or the final review found problems and created finding tickets. They are implemented like other tickets. This repeats up to the cycle limits in [Settings](settings.md#concurrency-review-and-retries).

## Stuck runs

**The tester says it could not test the application.**
It could not start your app. Put clear start instructions into **Run instructions** in [Settings](settings.md#tester) (command, port, setup). The app must listen on the port that the tester reserves. Then press **Retry**.

**Should I press Retry or Abort?**
**Retry** is safe and the usual choice once the cause is fixed. **Abort** cancels the whole run for good. See [Retry or Abort](needs-attention.md#retry-or-abort).

**The card says "Fixing automatically". Should I do something?**
No. WebDevLoop tries its own fix. The page updates by itself. If you do not want to wait, open **Don't want to wait? Show the actions**.

**Retry did not help, it stops with the same reason.**
The cause is not fixed yet. Read **What you can do** on the card and the **Technical details**.

**A ticket needs attention and I do not need it.**
Press **Skip**. The ticket counts as done without its change and tickets that depend on it continue. Use **Skip with dependents** to drop them too.

**The reviewers keep finding issues.**
After the review limit, the ticket asks for your decision. Read the findings. Skip the ticket if they are fine, or raise **Max review iterations per ticket** and **Retry**.

**The pull requests were closed without merging.**
The run needs attention. Reopen them and **Retry**, or abort the run and queue the spec again.

**The Troubleshooter costs too much model usage.**
Turn it off or lower the attempts in [Settings](settings.md#troubleshooter).

**An agent log shows "Permission denied".**
This is normal. Agents are not allowed to run some git commands, because WebDevLoop controls branches and GitHub itself. The agent is told and continues.

**I merged the stack on GitHub. When does the run complete?**
WebDevLoop checks GitHub about once a minute. When the base branch contains the top layer of the stack, the run is **Completed**.

**Where are my files?**
The clone and the ticket worktrees are on your machine. The **Repositories** page shows the local path of the clone. A ticket page shows its worktree path.

## Still stuck?

- Read the **Technical details** of the card.
- Check [Health](health.md).
- Read the step's [agent log](tickets-and-steps.md#agent-log).
- For bugs, open an issue in the WebDevLoop repository on GitHub and include the technical details. For how the app works inside, see the [developer guide](../developer-guide/index.md).
