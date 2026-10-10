# WebDevLoop

WebDevLoop is a local ASP.NET Core Blazor Server app that automates the "implement a spec" workflow on GitHub: you queue a
parent spec issue, and WebDevLoop snapshots its ticket sub-issues as a DAG, implements ready tickets concurrently with
Copilot agents on run-scoped branches, has every ticket reviewed (coding standards and specification) and fixed,
squash-merges each ticket into a run-scoped integration branch and publishes it as one layer of a stacked draft PR stack,
runs a parent review and a tester agent against the integrated app, marks the stack ready, and completes once a human
merged it. The app is the coordinator: state, queueing, Git/GitHub mutations, and recovery are app logic; agents only
explore, implement, review, resolve conflicts, and test locally. WebDevLoop does not split a spec into tickets: the spec must
already have open sub-issues, otherwise the run needs attention (add the sub-issues, then retry).

## Prerequisites

- .NET 11 SDK (`global.json` pins `11.0.100-rc.1.26425.128`).
- `git` on the `PATH` (LibGit2Sharp does the heavy lifting; the CLI is a checked fallback).
- The GitHub Copilot CLI: bundled into published builds; for `dotnet run` install it and set `WebDevLoop:Copilot:CliPath`.
- `playwright-cli` on the `PATH` for the tester agent (see the bundled `playwright-cli` skill).
- Optional: `gh` with the `gh stack` extension, only used when the stack REST API is unavailable
  (`WebDevLoop:GitHub:GhStackMode`).
- A GitHub App installed on the repositories, which you sign in with from the web UI (see [GitHub sign-in](#github-sign-in)).

Every prerequisite is checked at startup and on demand (Health page, `GET /api/prerequisites`). When one fails, the app
still starts, in **diagnostic-only mode**: the UI, `/api/health` (`503`), `/api/prerequisites`, the read endpoints, and the
OpenAPI document are served, mutating workflow endpoints answer `503` with the failing checks, and no workflow worker runs.
Fix the problem and press **Re-check** on the Health page; the workflow starts as soon as the checks pass. An invalid
configuration value (e.g. a non-positive interval or a malformed URL) fails fast at startup with a message
listing every problem.

## GitHub sign-in

WebDevLoop works on GitHub as **you**: you sign in from the web UI with a GitHub App (the
[web application flow](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app)),
and the resulting user access token is used for everything: the GitHub API (issues, pull requests, stacks), clone, fetch
and push, and the Copilot agent sessions. This is the
[GitHub OAuth setup of the Copilot SDK](https://docs.github.com/en/copilot/how-tos/copilot-sdk/setup/github-oauth) and
works for personal accounts, organization members, and enterprise (EMU) identities. Copilot usage is billed to your own
Copilot subscription; commits and pull requests are attributed to you ("via" the App).

What the token may do is the intersection of the App's permissions and your own access, limited to the repositories the
App is installed on, so the installation's repository selection controls which repositories WebDevLoop reaches.

### Create the GitHub App

1. Settings → Developer settings → GitHub Apps → New GitHub App (for an organization: the organization's Developer settings).
2. **Callback URL:** `https://localhost:7233/auth/github/callback` (add one per address you open WebDevLoop on, e.g. the
   `http` profile's `http://localhost:5240/auth/github/callback`). Keep **Expire user authorization tokens** checked:
   tokens then live 8 hours and WebDevLoop refreshes them automatically. **Webhook:** off (WebDevLoop polls).
3. Repository permissions:
   - **Contents**: Read and write (clone, push integration and stack branches)
   - **Issues**: Read and write (spec/ticket snapshots, finding sub-issues and dependencies, comments, closing tickets)
   - **Pull requests**: Read and write (draft PR layers, stacks, mark ready, merge tracking)
   - **Metadata**: Read (mandatory)

   No Copilot permission is needed: Copilot runs on the signed-in user's own subscription.
4. Note the **Client ID** and generate a **client secret**. No private key is needed.
5. **Install** the App on your account (and organizations) and select the repositories WebDevLoop may work on.

### Configure and sign in

```bash
cd src/WebDevLoop.Web
dotnet user-secrets set "WebDevLoop:GitHub:AppClientId" "Iv23..."
dotnet user-secrets set "WebDevLoop:GitHub:AppClientSecret" "<client secret>"
dotnet user-secrets set "WebDevLoop:GitHub:AppSlug" "<app-name-from-the-app-url>"   # optional, for the install link
```

(or the environment variables `WebDevLoop__GitHub__AppClientId` / `WebDevLoop__GitHub__AppClientSecret`; never commit
the secret). Start WebDevLoop: until you sign in, every page shows **Sign in with GitHub** and the app stays
diagnostic-only (the *GitHub authentication* check fails). The button sends you to GitHub to authorize the App and back
to the page you came from; the workflow starts as soon as the prerequisites pass. Signing out again stops new GitHub
work immediately.

The **GitHub** page shows who is signed in, which repositories the App's installations give WebDevLoop access to, and
which registered repositories it cannot reach. **Manage repository access** opens the installation's settings on GitHub,
where you add or remove repositories; the change applies right away. **Install the GitHub App on another account or
organization** adds an installation.

The sign-in survives restarts: the tokens are stored in `<DataDirectory>/github-credentials.dat`, encrypted with ASP.NET
Core data protection (keys in `<DataDirectory>/keys`; on Linux and macOS both are readable by your user only, and the keys
themselves are not encrypted at rest). The refresh token is valid for six months and is renewed with every refresh, so
you only sign in again after a long pause, after signing out, or when the App's authorization was revoked. Agent shell
commands never see the token: the tool policy denies reading credential variables, and the Copilot runtime receives the
token per session instead of through its environment.

## Configuration

Configuration uses the standard ASP.NET Core sources (`appsettings.json`, `appsettings.{Environment}.json`, user secrets in
Development, environment variables with `__` as separator, command-line arguments). All keys live in the `WebDevLoop`
section; `src/WebDevLoop.Web/appsettings.json` lists them with their defaults.

| Key | Default | Meaning |
| --- | --- | --- |
| `DataDirectory` | `<LocalApplicationData>/WebDevLoop` | Database, default workspace root (`workspaces/`), Copilot home (`copilot/`), UI state, encrypted GitHub sign-in (`github-credentials.dat`, `keys/`) |
| `DatabasePath` | `<DataDirectory>/webdevloop.db` | SQLite database (migrated automatically at startup) |
| `GitHub:ApiBaseUrl`, `GitHub:GraphQlUrl`, `GitHub:WebBaseUrl` | `https://api.github.com/`, `…/graphql`, `https://github.com/` | GitHub API and sign-in endpoints |
| `GitHub:AppClientId` | – | Client id of the GitHub App users sign in with |
| `GitHub:AppClientSecret` | – | A client secret of that App (secret) |
| `GitHub:AppSlug` | – | Optional: the App's URL name, for the "Install the GitHub App" link |
| `GitHub:GhStackMode` | `RestWithOptionalFallback` | `FallbackRequired` when the stack REST API is unavailable and `gh stack` must be installed |
| `GitHub:GhExecutable` | `gh` | `gh` CLI used for the stack fallback |
| `Copilot:CliPath` | – (bundled CLI) | Copilot CLI to launch |
| `Copilot:IdleTimeout` | `00:10:00` | Idle runtime eviction |
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
instructions, and the test port range. The global settings are seeded from the embedded defaults
(including the prompt templates) on first start. The workspace root and the Copilot home are global-only and startup-scoped:
they configure process-wide resources (the git workspace confines every clone and worktree path to the root it started
with), so they cannot be overridden per repository (the API rejects it) and a change of the global value takes effect after
a restart. Existing clones are not moved: after changing the workspace root, move them under the new root and update each
repository's local path (`PATCH /api/repos/{id}`); the startup log warns about every repository whose clone lies outside the
current root, and until fixed its tickets need attention. The repository
selected in the UI is remembered across restarts (`<DataDirectory>/ui-state.json`); it is view context only and never affects scheduling.

## Running

```bash
dotnet run --project src/WebDevLoop.Web                       # https://localhost:7233 (launch profile "https")
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
audited as `Control<Action>` run events (`ControlAutoRetry`/`ControlAutoSkip` when WebDevLoop resumed the work itself after a remediation).

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

## Needs attention: guidance and automatic remediation

WebDevLoop does not park work with "something went wrong". A run, ticket or step enters `NeedsAttention` only with a
structured `AttentionReason` (`src/WebDevLoop.Core/Domain/Attention`): a reason **code** (one per situation, see
`AttentionCode`), a plain-language **summary** and **why it matters**, the **cause** (`WebDevLoop`: WebDevLoop's own working
area or agents, `You`: something outside WebDevLoop to fix, `Decision`: you choose how to go on), the automatic fix it can
try, what was **already tried**, numbered **steps for you** (commands are copy-able) and the **actions** that make sense,
each with a one-line consequence. The technical wording (exception text, paths, hashes) stays available as `Details` and as
`FailureReason`. The reason is stored as JSON next to the run, ticket and step, shown through `attention` on their API views
and rendered as an **Action needed** card on the run, ticket and step pages; the dashboard alert and the queue show its
summary. The wording of every reason lives in one place, `AttentionReasons`; a guard test fails the build when work is parked
with a free-form string or a reason code has no factory.

Before the user is asked, every item that enters `NeedsAttention` goes through the resolution order
(`AttentionTriageService`), which records what it did as run events (`AttentionRaised`, `AttentionRemediationAttempted`,
`AttentionAutoResolved`, `AttentionNeedsYou`, `ControlAutoRetry`, `ControlAutoSkip`):

1. **Known automatic remediation** (`IKnownRemediation`, one per code, deterministic, bounded per item until the user's next
   Retry): a dirty ticket worktree is cleaned (tracked changes are saved as a patch in the run folder first) and verified;
   a ticket branch that does not contain the integration branch gets the integration tip merged (conflicts go to the user);
   a temporary GitHub or network failure while publishing is retried with a growing pause (30 s, 2 min, 5 min); a leftover
   integration branch of the same run is reset while nothing was integrated; a failed exploration is run once more; a
   reviewed ticket that adds nothing to the integration branch is skipped as "no changes needed". Interrupted steps are
   restarted after a restart by the agent-step recovery and recorded the same way; merge conflicts first go to the
   conflict-resolver agent. A remediation resumes the work exactly like the user's Retry would.
2. Further stages (e.g. a troubleshooter agent session) register another `IAttentionStage` after the known remediation.
3. The user, now with what was tried on the card.

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
