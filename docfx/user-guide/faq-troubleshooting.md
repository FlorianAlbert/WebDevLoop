# FAQ and troubleshooting

## Setup and sign-in

**Why can't I queue a spec? The form is disabled.**
WebDevLoop is in diagnostic-only mode. Open [Health](health.md), fix the failed checks and click **Re-check**. The most common cause is that you are not signed in to GitHub. See [GitHub](github.md).

**Health says "Agent models" failed. How do I fix it?**
The diagnostic lists each unavailable Model or unsupported Reasoning Effort with its role and settings scope.
Open [Settings, Agent roles](settings.md#agent-roles), select the global or repository scope, pick supported values
from the Model Catalog and save. Check every listed scope: one invalid repository override pauses all repositories.
Work resumes automatically when all checks pass. If no Model Catalog is available, use **Refresh models** to retry.
See [Agent models](health.md#agent-models).

**Why can't I edit the Model or Reasoning Effort?**
With no available Models, settings show **Models unavailable, retry** and the current values read-only. Sign in to
GitHub if needed, then use [Refresh models](settings.md#refresh-models). **Re-check** on Health only validates the
current Model Catalog; it does not fetch it. A failed refresh keeps the last-known Model Catalog and reports the error.

**The browser warns about the certificate.**
The app uses the .NET development certificate. Trust it once with `dotnet dev-certs https --trust`.

**GitHub shows a callback or redirect URL error when I sign in.**
The [public WebDevLoop GitHub App](https://github.com/apps/webdevloop) uses `https://localhost:7233/auth/github/callback`. Start with `dotnet run --project src/WebDevLoop.Web --launch-profile https` and open `https://localhost:7233`, not the HTTP address or `127.0.0.1`.

For your own GitHub App, the callback URL must match the address you opened. Add one callback URL for every address you use. To use a different address with the public App, you need a callback change by its owner, or your own App.

**I installed the public App, but WebDevLoop says sign-in is not configured.**
Installation grants repository access; it does not supply credentials to your local instance. The current sign-in flow still needs the chosen App's client ID and client secret. If you do not have credentials for the public App, create your own App. See [Getting started](getting-started.md#choose-a-github-app).

**My repository is not in the "Add repository" list.**
The GitHub App is not installed on it. On the [GitHub](github.md) page click **Manage repository access**, add the repository, then **Refresh**.

**A registered repository shows "Not accessible".**
The same cause. Add it to the App installation. See [GitHub](github.md#registered-repositories).

**Do I need to sign in again after a restart?**
No. The sign-in is stored encrypted on your machine and renews itself. You only sign in again after a long pause, after signing out, or if you revoked the App on GitHub.

## Specs and queue

**Why was my spec rejected before it appeared in the queue?**
The parent must be open and labeled `webdevloop`, and at least one open sub-issue needs that label independently.
Triage labels do not grant WebDevLoop Opt-in. Queueing checks current GitHub data, so a recent label removal or closure
can make a previously eligible issue ineligible. A GitHub lookup failure also queues nothing: check access or retry.
Eligible but blocked tickets do not prevent queueing. See [Queue](queue.md#queue-a-spec).

**I queued a spec and it says "The spec has no open tickets to implement".**
WebDevLoop does not split specs. Add the work as open sub-issues of the spec on GitHub, then press **Retry** on the run. See [Needs attention](needs-attention.md#the-spec-or-the-repository).

**My spec is "Queued" and nothing happens.**
It waits for a free slot. By default one spec per repository runs at a time. Check **Active slots** on the [Queue](queue.md#slots-and-parent-blockers) page. Raise **Max active specs per repository** in [Settings](settings.md#concurrency-review-and-retries) if you want more at once.

**My spec is "Waiting for dependency".**
The parent issue has an open native GitHub blocker. Every blocker must be closed, including external and cross-repository
issues; a completed WebDevLoop run does not override an open issue. Once all blockers close or are removed, the spec starts
from current trunk, even if a blocker's pull request is unmerged. Unavailable issues defer only the affected spec and show
**Blockers unavailable**; check access and refresh. See [Spec runs](runs.md#native-parent-blockers).

**A ticket stays "Blocked".**
It waits for every current native GitHub blocker to close, including unlabeled issues, issues outside the spec, and issues in other repositories. Local integration or skipped status cannot override an open issue. Check GitHub for the current relationships; unavailable information defers only the affected ticket and is logged for normal recovery. Unrelated tickets and running specs continue.

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
Abort the ticket or run. To change the intended work, update the issues on GitHub and queue a new run. Manual skipping is unavailable and aborting does not close blockers.

**The reviewers keep finding issues.**
After the review limit, the ticket asks for your decision. Read the findings, then raise **Max review iterations per ticket** and **Retry**, or abort the run to handle them manually.

**The pull requests were closed without merging.**
The run needs attention. Reopen them and **Retry**, or abort the run and queue the spec again.

**The Troubleshooter costs too much Model usage.**
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
