# WebDevLoop

WebDevLoop is a local ASP.NET Core Blazor Server app that automates the "implement a spec" workflow on GitHub: you queue a
parent spec issue, and WebDevLoop snapshots its ticket sub-issues as a DAG, implements ready tickets concurrently with
Copilot agents on run-scoped branches, has every ticket reviewed (coding standards and specification) and fixed,
squash-merges each ticket into a run-scoped integration branch and publishes it as one layer of a stacked draft PR stack,
runs a parent review and a tester agent against the integrated app, marks the stack ready, and completes once a human
merged it. The app is the coordinator: state, queueing, Git/GitHub mutations, and recovery are app logic; agents only
explore, implement, review, resolve conflicts, and test locally.

## Prerequisites

- .NET 11 SDK (`global.json` pins `11.0.100-rc.1.26425.128`).
- `git` on the `PATH` (LibGit2Sharp does the heavy lifting; the CLI is a checked fallback).
- The GitHub Copilot CLI: bundled into published builds; for `dotnet run` install it and set `WebDevLoop:Copilot:CliPath`.
- `playwright-cli` on the `PATH` for the tester agent (see the bundled `playwright-cli` skill).
- Optional: `gh` with the `gh stack` extension, only used when the stack REST API is unavailable
  (`WebDevLoop:GitHub:GhStackMode`).
- A GitHub App installed on the repositories (recommended), or a fine-grained PAT as fallback.

Every prerequisite is checked at startup and on demand (Health page, `GET /api/prerequisites`). When one fails, the app
still starts, in **diagnostic-only mode**: the UI, `/api/health` (`503`), `/api/prerequisites`, the read endpoints, and the
OpenAPI document are served, mutating workflow endpoints answer `503` with the failing checks, and no workflow worker runs.
Fix the problem and press **Re-check** on the Health page; the workflow starts as soon as the checks pass. An invalid
configuration value (e.g. a non-positive interval or a missing private key file) fails fast at startup with a message
listing every problem.

## GitHub App setup

1. Create a GitHub App (Settings → Developer settings → GitHub Apps → New GitHub App). No webhook or callback URL is
   needed; WebDevLoop polls.
2. Repository permissions:
   - **Contents**: Read and write (clone, push integration and stack branches)
   - **Issues**: Read and write (spec/ticket snapshots, finding sub-issues and dependencies, comments, closing tickets)
   - **Pull requests**: Read and write (draft PR layers, stacks, mark ready, merge tracking)
   - **Metadata**: Read (mandatory)
   - **Copilot requests**: Read and write, if the agents should run on the App's installation token (otherwise provide a
     user token for Copilot)
3. Generate a private key (`.pem`) and note the App's client id.
4. Install the App on the repositories WebDevLoop should work on.
5. Configure `WebDevLoop:GitHub:AppClientId` and either `WebDevLoop:GitHub:AppPrivateKeyPath` (path to the `.pem`) or
   `WebDevLoop:GitHub:AppPrivateKeyPem` (the key itself, e.g. from a secret store). Installation tokens are minted per
   repository and permission set in-process, cached, and refreshed before they expire; git credentials are resolved
   fresh for every clone, fetch, and push.

### PAT fallback

A fine-grained personal access token (Contents, Issues, Pull requests: read and write on the repositories) is used only
when the App cannot act (not configured, not installed, or minting failed) and the **PAT fallback** setting is enabled
(global settings, default on). Provide it as `WebDevLoop:GitHub:UserToken`, preferably through the environment variable
`WebDevLoop__GitHub__UserToken` or user secrets; never commit it. Agent sessions never receive a GitHub write token.

## Configuration

Configuration uses the standard ASP.NET Core sources (`appsettings.json`, `appsettings.{Environment}.json`, user secrets in
Development, environment variables with `__` as separator, command-line arguments). All keys live in the `WebDevLoop`
section; `src/WebDevLoop.Web/appsettings.json` lists them with their defaults.

| Key | Default | Meaning |
| --- | --- | --- |
| `DataDirectory` | `<LocalApplicationData>/WebDevLoop` | Database, default workspace root (`workspaces/`), Copilot home (`copilot/`), UI state |
| `DatabasePath` | `<DataDirectory>/webdevloop.db` | SQLite database (migrated automatically at startup) |
| `GitHub:ApiBaseUrl`, `GitHub:GraphQlUrl` | `https://api.github.com/`, `…/graphql` | GitHub API endpoints |
| `GitHub:AppClientId` | – | GitHub App client id |
| `GitHub:AppPrivateKeyPath` / `GitHub:AppPrivateKeyPem` | – | App private key (file path or PEM text, not both) |
| `GitHub:UserToken` | – | Fine-grained PAT for the fallback (secret) |
| `GitHub:GhStackMode` | `RestWithOptionalFallback` | `FallbackRequired` when the stack REST API is unavailable and `gh stack` must be installed |
| `GitHub:GhExecutable` | `gh` | `gh` CLI used for the stack fallback |
| `Copilot:CliPath` | – (bundled CLI) | Copilot CLI to launch |
| `Copilot:TokenRefreshSkew`, `Copilot:IdleTimeout` | `00:05:00`, `00:10:00` | Runtime replacement before token expiry; idle runtime eviction |
| `Tools:GitExecutable`, `Tools:PlaywrightCliExecutable` | `git`, `playwright-cli` | External tools checked at startup |
| `Workflow:Enabled` | `true` | `false` serves the UI/API only (no workers, e.g. a read-only dashboard) |
| `Workflow:ExplorationEnabled` | `true` | Run the explorer agent before tickets are dispatched |
| `Workflow:OutboxPollInterval` | `00:00:01` | Event dispatch polling while the outbox is empty |
| `Workflow:StartupRetryInterval` | `00:00:05` | How often startup recovery is retried while diagnostic-only |
| `Workflow:RecoveryInterval` | `00:02:00` | Periodic reconciliation (Git/GitHub state, stalled steps, queues, frontiers) |
| `Workflow:MergeTrackingInterval` | `00:01:00` | Polling of ready PR stacks for the human merge |
| `Workflow:RuntimeMaintenanceInterval` | `00:01:00` | Copilot runtime refresh and idle eviction |
| `Workflow:OutboxRetention`, `Workflow:OutboxPurgeInterval` | `7.00:00:00`, `06:00:00` | Dispatched events are deleted after the retention |
| `Workflow:StallGracePeriod` | `00:05:00` | Idle working tickets/specs are relaunched after this |
| `Workflow:ParkedIntegrationRetryInterval` | `00:15:00` | Retry of integrations parked after they moved the integration branch |
| `Workflow:TrunkContainmentTimeout` | `01:00:00` | A merged stack whose top layer never reaches trunk needs attention after this |
| `Workflow:TesterAppStartupTimeout` | `00:10:00` | How long the tester's app may take to listen on its reserved port |
| `AgentLogs:MaxEntriesPerStep`, `BatchSize`, `FlushInterval`, `PageSize` | `5000`, `50`, `00:00:01`, `1000` | Persisted live agent logs |

Workflow behaviour is edited in the app (Settings page or `/api/settings`) and stored in the database: global values with
nullable per-repository overrides for models, reasoning effort, prompt templates, timeouts, the active-spec limit, the
dependency mode (`WaitForMerge`/`StackOnTop`), implementer concurrency, review/retry/cycle limits, tester run
instructions, the test port range, and the PAT fallback. The global settings are seeded from the embedded defaults
(including the prompt templates) on first start. The workspace root and the Copilot home are global-only and startup-scoped:
they configure process-wide resources (the git workspace confines every clone and worktree path to the root it started
with), so they cannot be overridden per repository (the API rejects it) and a change of the global value takes effect after
a restart. Existing clones are not moved: after changing the workspace root, move them under the new root and update each
repository's local path (`PATCH /api/repos/{id}`); the startup log warns about every repository whose clone lies outside the
current root, and until fixed its tickets need attention. The PAT fallback is likewise read once at startup. The repository
selected in the UI is remembered across restarts (`<DataDirectory>/ui-state.json`); it is view context only and never affects scheduling.

## Running

```bash
dotnet run --project src/WebDevLoop.Web                       # http://localhost:5240 (launch profile "http")
WebDevLoop__DataDirectory=/srv/webdevloop dotnet run --project src/WebDevLoop.Web
dotnet publish src/WebDevLoop.Web -c Release -o out            # bundles the Copilot CLI and the agent skills
```

At startup the app migrates the database, seeds the global settings on first start, and evaluates the prerequisites.
When they pass, startup recovery reconciles Git/GitHub state, resumes interrupted agent sessions and integration sagas,
replays undispatched events, and recomputes queues and frontiers; then the hosted workers run: the event dispatcher,
periodic reconciliation, merge tracking, Copilot runtime maintenance, and event retention. Workflow work runs in the
background, each launch in its own DI scope; on shutdown running work is cancelled and awaited, and the next start resumes
it.

## REST API

The API lives under `/api`; the OpenAPI document is served at `/openapi/v1.json` and enums are strings.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/health`, `GET /api/prerequisites` | Readiness (`503` while diagnostic-only) and every check with its remediation |
| `GET/POST /api/repos`, `GET/PATCH/DELETE /api/repos/{repoId}`, `POST /api/repos/{repoId}/select` | Register and manage repositories (`cloneUrl` and `localPath` are optional); select the UI context |
| `GET/PUT /api/settings/global`, `GET/PUT /api/repos/{repoId}/settings`, `GET /api/settings/effective/{repoId}` | Global settings, per-repository overrides (empty = inherit), effective values |
| `POST /api/repos/{repoId}/spec-runs` with `{"specIssueNumber": 42}`, `GET /api/repos/{repoId}/spec-runs` | Queue a parent spec issue; list the repository queue |
| `GET /api/spec-runs/{id}` and `…/tickets`, `…/events`, `…/stack`, `…/merge-status` | Run state, ticket DAG, audit events, PR stack layers, merge status |
| `GET /api/ticket-runs/{id}` and `…/steps`, `GET /api/steps/{id}` and `…/logs` | Ticket and agent step details, persisted agent logs |
| `POST /api/spec-runs/{id}/retry`, `…/abort`; `POST /api/ticket-runs/{id}/retry`, `…/skip`, `…/abort` | Run controls (see below) |
| `GET /api/events/stream`, `GET /api/spec-runs/{id}/events/stream` | Live updates as server-sent events |

## Copilot runtime and bundled skills

Agents run through the GitHub Copilot SDK (`GitHub.Copilot.SDK`). `dotnet publish` bundles the SDK's pinned Copilot CLI
runtime into the published app (`runtimes/<rid>/native`), so a published WebDevLoop ships everything it needs. Development
builds (`dotnet build`/`dotnet run`) stay offline and do not download it: set `WebDevLoop:Copilot:CliPath` to an installed
`copilot` CLI, or bundle the runtime into a dev build with `dotnet build -p:CopilotSkipCliDownload=false`.

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

## Build and test

```bash
dotnet build WebDevLoop.slnx
dotnet test --solution WebDevLoop.slnx
```

The test projects use xUnit v3 with Microsoft.Testing.Platform configured in `global.json`. Tests use fakes only (no real
GitHub, Copilot, or network). `tests/WebDevLoop.Web.Tests/Workflow` runs the whole app host end to end with faked GitHub
and Copilot against a local bare git remote: queue → concurrent implementation with a continuous frontier → review/fix →
PR stack → parent review → tester → ready → merge → completed, including a stacked dependent spec, a lost GitHub
response, and an app restart in the middle of a run.
