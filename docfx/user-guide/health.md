# Health

The Health page shows the prerequisite checks. They decide whether WebDevLoop may do real work.

![The health page with all checks passed](images/health.png)

## What you can do here

- See whether WebDevLoop is **Operational** or in **Diagnostic-only** mode.
- See which check failed and how to fix it.
- Run all checks again with **Re-check**.

## Readiness mode

| Mode | Meaning |
| --- | --- |
| Operational | All checks passed (warnings are allowed). The workflow runs. |
| Diagnostic-only | At least one check failed, or the checks have not run yet. |

The page also shows when the checks last ran.

## Diagnostic-only mode

In this mode the app still starts, so you can see what is wrong. You can still:

- open every page and read all runs, tickets, steps and settings,
- edit and save settings to fix the configuration,
- use the read endpoints of the API.

You cannot:

- queue a spec (the form is disabled),
- use Retry, Skip or Abort (the buttons are disabled),
- start new work. Workflow workers wait and new agent launches pause; in-flight steps can finish.

A yellow banner on every page says "Diagnostic-only mode" and links here. The Health page itself says "Mutating workflow is disabled".

To leave the mode, fix the failed checks and click **Re-check**. For **Agent models**, saving settings or using **Refresh models** re-validates automatically. The workflow resumes as soon as all checks pass. You do not need to restart the app, except for a wrong configuration value. The app refuses to start when a configuration value is invalid, for example a malformed URL. The error message lists every problem.

## The checks

Failed checks are listed first. Each row has a status (**Passed**, **Warning** or **Failed**), the name of the check and details. A check that is not passed also shows a **Fix** hint.

| Check | What it verifies |
| --- | --- |
| Workspace root | The folder for clones and worktrees exists and is writable. |
| SQLite database | The database can be reached and is migrated. |
| Bundled skills | The agent skills that ship with WebDevLoop are all present. |
| GitHub authentication | You are signed in to GitHub. Fix it on the [GitHub](github.md) page. |
| Agent models | Every role's effective Model is in the Model Catalog and its Reasoning Effort is supported, globally and in every repository override. |
| LibGit2Sharp | The native git library loads. |
| git CLI | `git` is installed and on the `PATH`. |
| gh stack | The GitHub CLI and its `gh stack` extension. This is a **Warning** when missing, because it is only needed when the stack REST API is not available. It is **Failed** when the configuration requires the fallback. |
| Copilot runtime | The Copilot CLI starts. Set `WebDevLoop:Copilot:CliPath` when you run with `dotnet run`. |
| playwright-cli | `playwright-cli` is installed. The tester agent needs it. |
| Test port range | The port range for the tester is valid. |

Long paths in the details are shortened. Hover over the text to see the whole message.

## Agent models

The **Model Catalog** contains the Models available to your signed-in GitHub account through Copilot, using the same identity as the agents.
**Agent models** runs after **GitHub authentication**. If you are not signed in, fix the sign-in check first; Model validation is deferred.

The check fails if the Model Catalog has never been fetched successfully, a configured Model is missing, or a
configured Reasoning Effort is unsupported. Reasoning Effort is not applicable for a Model without reasoning support.
It checks global settings and the effective settings of every repository override, including inherited values.
One invalid role in any repository pauses new work across **all repositories**, not just that repository.

The diagnostic names each offending role, Model and global or repository scope. Use its settings link to fix every listed problem:

1. Open [Settings, Agent roles](settings.md#agent-roles) and choose the scope in **Edit settings for**.
2. Pick an available Model and a supported Reasoning Effort for each affected role. Fix global settings too, even if a repository overrides them.
3. Click **Save settings**. The Model Catalog is fetched again and all scopes are validated. Work resumes automatically once all prerequisite checks pass.

Shipped defaults are not automatically replaced when unavailable. On a fresh install, choose an available Model if
your account does not offer a default. If the Model Catalog cannot be fetched, use
[Refresh models](settings.md#refresh-models) to retry; settings stay reachable.

## Re-check

Click **Re-check** after you fixed something. It runs all checks again and updates the mode, using the current
Model Catalog without fetching it. To fetch current Models, use **Refresh models** in [Settings](settings.md#refresh-models).
