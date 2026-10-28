# WebDevLoop

WebDevLoop is a local ASP.NET Core Blazor Server app that automates the "implement a spec" workflow on GitHub: you queue a
parent spec issue, and WebDevLoop snapshots its ticket sub-issues as a DAG, implements ready tickets concurrently with
Copilot agents on run-scoped branches, has every ticket reviewed (coding standards and specification) and fixed,
squash-merges each ticket into a run-scoped integration branch and publishes it as one layer of a stacked draft PR stack,
runs a parent review and a tester agent against the integrated app, marks the stack ready, and completes once a human
merged it. The app is the coordinator: state, queueing, Git/GitHub mutations, and recovery are app logic; agents only
explore, implement, review, resolve conflicts, test, and troubleshoot locally. WebDevLoop does not split a spec into tickets: the spec must
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
OpenAPI document are served, mutating workflow endpoints answer `503` with the failing checks, and no new workflow work starts.
Settings remain editable. If validation fails while running, new agent launches pause and in-flight steps finish.
Fix the problem and press **Re-check** on the Health page; the workflow resumes as soon as the checks pass. An invalid
configuration value (e.g. a non-positive interval or a malformed URL) fails fast at startup with a message
listing every problem.

The **Agent models** check runs after **GitHub authentication** and validates every role's effective Model and
Reasoning Effort in global settings and every repository override against the **Model Catalog** available to your
signed-in GitHub account through Copilot. A never-fetched Model Catalog, an unknown Model or an unsupported
Reasoning Effort keeps the whole app diagnostic-only; the diagnostic names each offending role, Model and scope and
links to Settings. Choose available values and save: validation resumes work automatically once all checks pass.
If the Model Catalog cannot be fetched, use **Refresh models** in Settings to retry; Health's **Re-check** only
validates the current Model Catalog.

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

### Use the public WebDevLoop GitHub App

The [WebDevLoop GitHub App](https://github.com/apps/webdevloop) is public. Install it on your account or organization
and select the repositories WebDevLoop may work on.

Its callback URL is `https://localhost:7233/auth/github/callback`. Start the app with the `https` launch profile
and open `https://localhost:7233` when signing in:

```bash
dotnet run --project src/WebDevLoop.Web --launch-profile https
```

Installing the public App grants repository access; it does **not** configure your local WebDevLoop instance.
The current sign-in flow still requires that App's client ID and client secret in local configuration.
Do not publish or commit the secret. If you do not have credentials for the public App, create your own App instead.
For the public App, set `WebDevLoop:GitHub:AppSlug` to `webdevloop` so the installation link points to it.

### Create your own GitHub App

Use your own App if you need your own credentials or a different callback URL.

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
nullable per-repository overrides for Models, Reasoning Effort, prompt templates, timeouts, the active-spec limit,
implementer concurrency, review/retry/cycle limits, tester run
instructions, the test port range, and the Troubleshooter switch and attempt limit. The global settings are seeded from the embedded defaults
(including the prompt templates) on first start. The workspace root and the Copilot home are global-only and startup-scoped:
they configure process-wide resources (the git workspace confines every clone and worktree path to the root it started
with), so they cannot be overridden per repository (the API rejects it) and a change of the global value takes effect after
a restart. Existing clones are not moved: after changing the workspace root, move them under the new root and update each
repository's local path (`PATCH /api/repos/{id}`); the startup log warns about every repository whose clone lies outside the
current root, and until fixed its tickets need attention. The repository
selected in the UI is remembered across restarts (`<DataDirectory>/ui-state.json`); it is view context only and never affects scheduling.

In global Settings and repository overrides, pick each role's Model by display name from the Model Catalog; choices
also show the Model ID, which remains the stored value. Reasoning Effort choices follow the effective Model's supported
efforts and are disabled for Models without reasoning support. Repository overrides can inherit the global Model and
Reasoning Effort independently when those inherited values are supported.
**Refresh models** fetches current Models and re-validates settings without restarting. Without a Model Catalog,
current Model and Reasoning Effort values are read-only with **Models unavailable, retry**; failed refreshes keep the
last-known Model Catalog. The Model Catalog is fetched at startup, after GitHub sign-in, after settings saves and on
manual refresh; it is held only in memory and is not refreshed periodically.

The shipped default Models are `claude-sonnet-5.5` for standard roles and `claude-opus-5.5` for review roles.
If a default is missing from your Model Catalog, choose an available Model in Settings; no automatic substitution
occurs. Existing stored Model IDs and recorded launches remain unchanged, with no migration.

If a Model disappears during an agent launch or turn, the runner confirms its loss against a freshly fetched Model Catalog.
The step goes to **Needs attention** with `ModelUnavailable`, new launches pause, and no retry budget is consumed.
Use **Open Settings** on the Action needed card, choose an available Model and supported Reasoning Effort for the
affected role, and save. Once validation passes, return to the ticket or run and press **Retry**; saving does not
automatically retry the parked phase. See the [user guide](docfx/user-guide/needs-attention.md#modelunavailable).

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
| `GET/POST /api/repos/{repoId}/opt-in` | Check the fixed `webdevloop` repository label; create it only with explicit `{"approveLabelCreation": true}` consent |
| `GET/PUT /api/settings/global`, `GET/PUT /api/repos/{repoId}/settings`, `GET /api/settings/effective/{repoId}` | Global settings, per-repository overrides (empty = inherit), effective values |
| `POST /api/repos/{repoId}/spec-runs` with `{"specIssueNumber": 42}`, `GET /api/repos/{repoId}/spec-runs` | Queue an eligible parent spec issue (WebDevLoop Opt-in required); list the repository queue |
| `GET /api/spec-runs/{id}` and `…/tickets`, `…/events`, `…/stack`, `…/merge-status` | Run state, ticket DAG, audit events, PR stack layers, merge status |
| `GET /api/ticket-runs/{id}` and `…/steps`, `GET /api/steps/{id}` and `…/logs` | Ticket and agent step details, persisted agent logs |
| `POST /api/spec-runs/{id}/retry`, `…/abort`; `POST /api/ticket-runs/{id}/retry`, `…/abort` | Run controls (see below) |
| `GET /api/events/stream`, `GET /api/spec-runs/{id}/events/stream` | Live updates as server-sent events |

### Parent blockers

Each scheduling decision re-reads the parent issue and its current native GitHub blockers. Every open blocker holds
the spec, including issues outside the spec or repository, regardless of opt-in or completed WebDevLoop runs.
Unavailable issues defer only the affected spec with an actionable, run-specific queue-recovery error; normal recovery
retries the decision while independent queued specs can still start. Queue and run pages retain local data and show
**Blockers unavailable** rather than treating unknown state as no blockers.
Once every blocker is closed (or its relationship removed), a new spec starts from current trunk even if a blocker's
pull request is unmerged. Closing that issue before merging accepts that its changes may not be on trunk.
Queue order cannot bypass blockers. Cross-spec stacking is not supported or configurable; ticket PR stacks within
each spec remain unchanged.

### Repository label consent

Repository registration checks the fixed `webdevloop` label. If missing, `POST /api/repos` returns `409` with
`code: "label_creation_approval_required"`; resubmit with `approveLabelCreation: true` only after user approval.
Omitted or false approval never creates the label or connects a repository whose label is missing.
Creating the label does not label existing issues. Label lookup/creation errors return actionable `503` problems,
not an approval prompt; check GitHub access and Issues read/write permissions and retry.

Connected repositories retain all configuration when the label is missing or deleted. New work pauses at decision
points while already selected tickets finish. `GET /api/repos/{repoId}/opt-in` checks without changing GitHub;
`POST` uses the same explicit approval field to create the label and let normal reconciliation resume work.
The [Repositories user guide](docfx/user-guide/repositories.md#webdevloop-opt-in-label) explains the UI consent and retry flow.

### Queue eligibility (WebDevLoop Opt-in)

The parent spec must be **open** and labeled **`webdevloop`**, with at least one **open sub-issue independently
labeled `webdevloop`**. Opting in a parent does not opt in its children, and triage labels such as `ready-for-agent`
are not runtime permission. Eligible tickets may all be blocked; that does not prevent queueing.

The API and the typed issue-number or URL form share the same enqueue path, which fetches current GitHub state and
labels at submission. An ineligible spec is rejected before creating a run (`409` with a reason). A failed GitHub
lookup, including authorization or transient failures, fails closed (`503` with access/retry guidance); stale data
never authorizes queueing. New runs return `202`; duplicate unfinished runs return `200 AlreadyQueued` and the existing
run id without creating another run. Diagnostic-only safeguards still apply.

### Parent review and testing scope

Parent review and application testing assess only tickets actually integrated into the spec branch, not every requirement
or sub-issue of the parent spec. Both review axes and the tester receive the integrated tickets' titles and requirement
snapshots, with the parent spec providing applicable constraints. This scope is supplied even with custom prompt templates.
Removing a ticket's opt-in after its work was selected does not remove its integrated changes from review or testing.

Agents may acknowledge omitted work in their summaries, but absent excluded requirements must not cause a failure or be
recreated as finding issues. Genuine defects, regressions, or unmet requirements in integrated work still follow the normal
finding-ticket workflow. Once those fixes are integrated, subsequent review and testing include them too.

New parent-review and tester finding issues are created with `webdevloop` and linked as sub-issues of their parent,
so they can enter the normal ticket workflow. Reused findings, including issues recovered after a crash or retried
issuance, keep their current labels: WebDevLoop never silently reverses a maintainer's opt-out.

Finding creation, labeling, or linking failures are reported rather than treated as successful issuance. If GitHub
creates an issue but drops its label, check the repository label and the app's issue-label permissions, then opt the
issue in explicitly. Recovery uses its hidden finding fingerprint to reuse the existing issue without duplicating
it or changing its labels.

## Copilot runtime and bundled skills

Agents run through the GitHub Copilot SDK (`GitHub.Copilot.SDK`). `dotnet publish` bundles the SDK's pinned Copilot CLI
runtime into the published app (`runtimes/<rid>/native`), so a published WebDevLoop ships everything it needs. Development
builds (`dotnet build`/`dotnet run`) stay offline and do not download it: set `WebDevLoop:Copilot:CliPath` to an installed
`copilot` CLI, or bundle the runtime into a dev build with `dotnet build -p:CopilotSkipCliDownload=false`.

Agent skills (mattpocock/skills and playwright-cli) are source-controlled under
`src/WebDevLoop.Infrastructure/Skills/Bundled` with `skills-manifest.json` (origin, license, expected files) and their
license texts, and are copied to `skills/` in the app output.

## Ticket blockers and run controls (Retry / Abort)

Before each ticket-frontier decision, WebDevLoop re-reads the ticket's current native GitHub blockers and their issue
states. Every blocker must be **closed**, even if it is not opted in, is outside the parent spec, or belongs to another
repository. Local integration or skipped status cannot release an open blocker; a closed issue releases dependents
regardless of its pull request's merge state. Relationship changes take effect at the next decision, without continuous
monitoring. A missing or unavailable issue or blocker defers only the affected ticket, reports an error in the application log,
and is retried by normal periodic recovery. Unrelated tickets and running specs continue. The saved ticket DAG is for display,
not dispatch authorization.

Runs and tickets that need attention are resumed by the user from the run/ticket pages or the API
(`POST /api/spec-runs/{id}/retry|abort`, `POST /api/ticket-runs/{id}/retry|abort`). The commands are rejected with
`503` and the failing prerequisites in diagnostic-only mode, are compare-and-swap safe (`409` on a lost race), and are
audited as `Control<Action>` run events (`ControlAutoRetry` when WebDevLoop resumed the work itself after a remediation).

- **Retry a spec** resumes the phase that failed: preparation, parent review, testing, or merge tracking (restarted
  from `ReadyForReview`, which gives trunk a fresh containment window). If the last
  parent-review/test cycle created finding tickets that are still open, the spec returns to `Running` first. Active
  phases claim a free active-spec slot (`409` while all `MaxActiveSpecsPerRepo` slots are taken).
- **Retry a ticket** implements it again, starts a fresh review round (full `MaxReviewIterations` budget), or resumes its
  integration saga, depending on where it failed. A ticket whose saga is already past the squash (its commit may be on
  the integration branch) always resumes the saga.
- **Abort** stops agent sessions, cancels active steps, and (for a spec) aborts its open tickets, kills the tester app,
  releases its test lease and active slot, and cleans up its worktrees. Aborting does not close GitHub blockers.

Manual **Skip** and **Skip with dependents**, their API operation (`POST /api/ticket-runs/{id}/skip`), and automatic
attention skips are removed. Retry, step cancellation, and abort remain available. Externally closed or detached unstarted
tickets may still appear as `Skipped` in reconciliation history, but this never overrides an open native GitHub blocker.

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
`AttentionTroubleshooterStarted`, `AttentionTroubleshooterFinished`, `AttentionAutoResolved`, `AttentionNeedsYou`,
`ControlAutoRetry`):

1. **Known automatic remediation** (`IKnownRemediation`, one per code, deterministic, bounded per item until the user's next
   Retry): a dirty ticket worktree is cleaned (tracked changes are saved as a patch in the run folder first) and verified;
   a ticket branch that does not contain the integration branch gets the integration tip merged (conflicts go to the user);
   a temporary GitHub or network failure while publishing is retried with a growing pause (30 s, 2 min, 5 min); a leftover
   integration branch of the same run is reset while nothing was integrated; a failed exploration is run once more.
   A reviewed ticket that adds nothing to the integration branch requires a user decision, not an automatic skip. Interrupted steps are
   restarted after a restart by the agent-step recovery and recorded the same way; merge conflicts first go to the
   conflict-resolver agent. A remediation resumes the work exactly like the user's Retry would.
2. **Troubleshooter agent session** (`TroubleshooterStage`, an `IAttentionStage` registered after the known remediation):
   one bounded agent session for tickets whose reason needs judgement. Further stages register another `IAttentionStage`
   after it.
3. The user, now with what was tried and the troubleshooter's diagnosis on the card.

### The Troubleshooter

The Troubleshooter is an agent role like the others: Settings → Agent roles has its own Model, Reasoning Effort, timeout and
prompt template (`Resources/Prompts/Troubleshooter.md`, defaults, validation and global/per-repository inheritance work as for
every role). Settings → Troubleshooter has **Try to resolve problems automatically with an agent** (default on; global switch
with a per-repository override) and **Troubleshooter attempts per problem** (default 2), because every attempt costs Model usage.

- **Which problems escalate.** The decision is encoded per reason code in `TroubleshooterEscalation` (every code has an
  explicit rule and rationale; a test fails when a code has none). Escalating: `ImplementationFailed`, `ImplementerBlocked`,
  `WorktreeNotClean`, `TicketBranchNotBasedOnIntegration` and `ReportedCommitMismatch` (after the known remediation could not
  repair them), `ReviewFailed`, `FixFailed`, `IntegrationFailed`, `MergeConflictUnresolved` (after the conflict resolver gave up)
  and `InterruptedRepeatedly` — unexpected git states, an agent that failed or reported "blocked", an integration that failed
  with an unfamiliar error. Never escalating: deterministic cases with a fixed instruction (`PromptNotRenderable`, spec and
  settings problems), decisions (`ReviewIterationsExhausted`, `*CycleLimit`, `NoNewWork`, `TicketHasNoChanges`,
  `PullRequestsClosedUnmerged`), temporary outages (`IntegrationTemporaryFailure`), anything that is GitHub or remote state
  (moved or rejected integration branch, stack and pull request problems, `StackBranchExists`) and problems of the whole run,
  which have no ticket worktree to repair and no check to verify a repair with.
- **Bounds.** One session per attempt with the role's timeout; at most *max attempts* sessions per ticket and reason code since
  the user's last Retry; and a **state fingerprint** (reason code plus the git state of the ticket worktree and branches): a
  problem in an unchanged state is never escalated twice, the earlier diagnosis is shown again instead. Pressing Retry or
  Abort while a session runs cancels it (its Copilot session is aborted), so it cannot keep changing a worktree that resumed work
  uses.
- **Input.** The prompt carries the reason code, summary and technical details, the failed phase, the ticket and spec text, the
  worktree paths, the tail of the last agent step's log, `git status` / recent commits / branch tips of the ticket worktree,
  the integration branch and trunk (local and as of the last fetch), the pull request state WebDevLoop knows, and everything
  already tried. The same material is written to a read-only context folder in the run folder.
- **Capabilities.** Same sandbox as the other roles (`RoleCapabilityPolicies`): it may read, edit and commit in the ticket
  worktree and in a scratch checkout of the integration tip it is given (its own branch, thrown away afterwards), run git and
  the repository's build and test commands there and write backups to the run folder. It receives no GitHub token, and `gh`,
  `git push`, `fetch`, `pull`, `remote`, `config`, `worktree`, `branch`, `update-ref`, `tag`, `reflog`, `gc` and friends are
  denied, as is checking out, switching to or rebasing the integration and trunk branches. It cannot force-push, delete remote
  branches or pull requests, change settings, touch other runs or merge; such steps stay with the user. GitHub state is read by
  WebDevLoop and handed to the agent.
- **Safety net.** Every shell command and tool call lands in the step log. Before the session WebDevLoop saves the uncommitted
  changes of the ticket worktree as a patch, the list of untracked files and the branch tips in
  `<run folder>/troubleshooter/backups/<ticket>/…`, so nothing is lost silently.
- **Report and verification.** The agent ends with the `report_troubleshooting` tool:
  `{ outcome: resolved | needs_user | cannot_resolve, summary, actions_taken[], verification, user_steps[], suggested_buttons[] }`.
  A `resolved` outcome is only a claim: WebDevLoop re-runs the failed check itself (`TroubleshooterVerifier`: the ticket
  worktree is a clean checkout of the ticket branch, no commit of the branch was lost — a rewritten branch is restored —
  and, for new commits or branch problems, the branch contains the integration tip). New commits are reviewed before the work
  continues; otherwise the failed phase resumes like the user's Retry. A claim that does not hold is discarded and recorded.
- **Visibility.** The session is a normal step (role *Troubleshooter*, kind *Troubleshoot*, Model, status, log) on the ticket
  and run pages and in the run's timeline (`AttentionTroubleshooterStarted` / `AttentionTroubleshooterFinished`). When it does
  not resolve the problem, its diagnosis, the actions it took, its steps for you and its suggested buttons populate the
  **Action needed** card ("What the Troubleshooter found", "What WebDevLoop already tried", "What you can do").

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
