# WebDevLoop

WebDevLoop is a local ASP.NET Core Blazor Server app for orchestrating GitHub issue implementation workflows.

## Build and test

Requires the .NET 11 SDK (this repo pins `11.0.100-rc.1.26425.128` in `global.json`).

```bash
dotnet build WebDevLoop.slnx
dotnet test
```

The test projects use xUnit v3 with Microsoft.Testing.Platform configured in `global.json`.

## Copilot runtime and bundled skills

Agents run through the GitHub Copilot SDK (`GitHub.Copilot.SDK`). Builds do not download the Copilot CLI runtime by
default; configure `CopilotRuntimeOptions.CliPath` to an installed `copilot` CLI, or bundle the SDK's pinned runtime
into the app output with `dotnet build -p:CopilotSkipCliDownload=false`.

Agent skills (mattpocock/skills and playwright-cli) are source-controlled under
`src/WebDevLoop.Infrastructure/Skills/Bundled` with `skills-manifest.json` (origin, license, expected files) and their
license texts, and are copied to `skills/` in the app output.

## Run controls (Retry / Skip / Abort)

Runs and tickets that need attention are resumed by the user from the run/ticket pages or the API
(`POST /api/spec-runs/{id}/retry|abort`, `POST /api/ticket-runs/{id}/retry|skip|abort`). The commands are rejected with
`503` and the failing prerequisites in diagnostic-only mode, are compare-and-swap safe (`409` on a lost race), and are
audited as `Control<Action>` run events.

- **Retry a spec** resumes the phase that failed: preparation, parent review, testing, or merge tracking (restarted
  from `ReadyForReview`, which gives trunk a fresh containment window). If the last
  parent-review/test cycle created finding tickets that are still open, the spec returns to `Running` first. Active
  phases claim a free active-spec slot (`409` while all `MaxActiveSpecsPerRepo` slots are taken).
- **Retry a ticket** implements it again, starts a fresh review round (full `MaxReviewIterations` budget), or resumes its
  integration saga, depending on where it failed. A ticket whose saga is already past the squash (its commit may be on
  the integration branch) always resumes the saga.
- **Skip a ticket** (only while `Blocked`, `Ready`, or `NeedsAttention`): a skipped blocker counts as done, so its
  dependents start on an integration branch without the skipped change. Send `{"dependents":"Skip"}` to skip every
  not-yet-started dependent as well. A ticket whose squash commit is already on the integration branch must be retried.
- **Abort** stops agent sessions, cancels active steps, and (for a spec) aborts its open tickets, kills the tester app,
  releases its test lease and active slot, and cleans up its worktrees. An aborted ticket never unblocks its dependents.

`GET /api/spec-runs/{id}/merge-status` reports the PR stack as `Awaiting`, `Merged`, `Closed` (closed unmerged or never
reached trunk), `NotReady`, `CompletedWithoutPullRequests`, or `Aborted`.
