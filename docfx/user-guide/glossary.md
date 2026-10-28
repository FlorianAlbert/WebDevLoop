# Glossary

| Term | Meaning |
| --- | --- |
| Spec | A GitHub issue that describes a feature and has open sub-issues. It is the unit you queue. |
| Parent issue | Another word for the spec issue, the parent of its tickets. |
| Ticket | A sub-issue of the spec. One ticket is one piece of work for an agent. |
| Finding ticket | A ticket that WebDevLoop creates for a problem found by the final review or the tester. |
| Spec run | One pass of WebDevLoop over one spec. It has a status and its own branches. |
| Ticket run | The work on one ticket inside a spec run. Its page is the ticket page. |
| Step | One agent session or app action on a ticket or run, for example Implement or Review. |
| Agent | A Copilot session with a fixed job (role). |
| Role | The job of an agent: Explorer, Implementer, Reviewer (coding standards), Reviewer (specification), Conflict resolver, Tester or Troubleshooter. |
| Model | An AI model an agent role can run on, identified by a stable ID and shown under a display name. |
| Model Catalog | The set of Models currently available to the signed-in GitHub account, as reported by Copilot. |
| Reasoning Effort | How much thinking a Model is asked to spend on a step; each Model supports its own subset of efforts. |
| Explorer | The agent that looks at the repository before tickets start. |
| Troubleshooter | The agent that looks into stuck tickets before you are asked. |
| DAG | The ticket graph: tickets are nodes and "blocked by" links are arrows. It has no circles. |
| Blocked by | A GitHub link that says a ticket (or spec) must be done first. |
| Layer (ticket table) | The depth of a ticket in the DAG. `0` has no blockers. |
| Frontier | Unstarted tickets whose current native GitHub blockers are all closed. |
| Base branch | The trunk branch of the repository, usually `main`. Runs start from it and the stack is merged into it. Also called trunk. |
| Integration branch | A branch per run (`webdevloop/<run id>/integration`) that collects the squash commits of all finished tickets. |
| Ticket branch | A branch per ticket where the agent writes the change. |
| Worktree | A separate local folder of the clone where one ticket is worked on. |
| Squash commit | One commit that holds all changes of a ticket. It goes on the integration branch. |
| Integration | Putting a finished ticket into the integration branch and publishing it as a pull request. |
| Integration saga | The list of small steps of one integration. WebDevLoop continues from the last finished step after a failure. |
| Stack layer | One draft pull request of the stack. Each layer is based on the one below. Layer 1 targets the base branch. |
| Stack (PR stack) | The chain of stacked draft pull requests of one run, one per ticket. |
| Draft PR | A pull request marked as draft. The stack becomes ready for review at the end of the run. |
| Review iteration | One round of review and fixes of a ticket. |
| Parent review | The final review of the whole integrated spec. |
| Cycle | One round of parent review or testing that may create finding tickets. The limits are in Settings. |
| Tester | The agent that starts the integrated app and tests it with `playwright-cli`. |
| Active slot | The right to work on a spec. By default each repository has one. |
| Parent blocker | A native GitHub "blocked by" issue of the parent. Every blocker must be closed before a new spec starts from current trunk. |
| Awaiting merge | The stack is open and waits for you to merge it. |
| Needs attention | A run, ticket or step stopped and needs you. It shows a reason and the steps to continue. |
| Action needed | The card that explains a Needs attention state and has the buttons. |
| Retry | Resume the phase that stopped. |
| Skipped | Reconciliation retired an unstarted ticket closed or detached on GitHub. Not a manual command and never permission to bypass an open blocker. |
| Abort | Stop a ticket or run for good. |
| Override | A repository setting that replaces the global value. |
| Inherited | A repository setting that uses the global value. |
| Diagnostic-only mode | The state where a prerequisite check failed or has not run yet. Pages and settings stay reachable, but no new work starts. |
| Prerequisite check | A test on the Health page, for example "git CLI" or "GitHub authentication". |
| GitHub App | The app on GitHub that you sign in with. It decides which repositories WebDevLoop may reach. |
| Current repository | The repository selected in the sidebar. It only decides what the Queue and Settings pages show. |
| Workspace root | The folder with all clones and worktrees. |
| Event (timeline) | A line in the run's history, like "Ticket is now integrated". |
