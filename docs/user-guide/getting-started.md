# Getting started

This page takes you from a fresh checkout to your first queued spec.

## Prerequisites

- The .NET 11 SDK (the repository's `global.json` pins the exact version).
- `git` on your `PATH`.
- The GitHub Copilot CLI. A published build bundles it. With `dotnet run` you install it yourself and set `WebDevLoop:Copilot:CliPath`.
- `playwright-cli` on your `PATH`. The tester agent uses it.
- A GitHub account with a Copilot subscription. Copilot usage is billed to you.
- Optional: `gh` with the `gh stack` extension. It is only used when the stack REST API is not available.

The [Health](health.md) page checks all of this for you.

## Create the GitHub App

WebDevLoop signs in to GitHub with a GitHub App that you own. In short:

1. On GitHub, create a new GitHub App (Settings, Developer settings, GitHub Apps).
2. Set the callback URL to `https://localhost:7233/auth/github/callback`. Turn the webhook off.
3. Give it repository permissions: Contents (read and write), Issues (read and write), Pull requests (read and write), Metadata (read).
4. Note the **Client ID** and create a **client secret**.
5. Install the App on your account and select the repositories it may use.

Then store the client id and secret as user secrets. Do not commit them:

```bash
cd src/WebDevLoop.Web
dotnet user-secrets set "WebDevLoop:GitHub:AppClientId" "<client id>"
dotnet user-secrets set "WebDevLoop:GitHub:AppClientSecret" "<client secret>"
```

The full details are in the [README](https://github.com/FlorianAlbert/WebDevLoop#readme) (sections "GitHub sign-in" and "Configuration").

## Start WebDevLoop

From the repository root:

```bash
dotnet run --project src/WebDevLoop.Web
```

This uses the `https` launch profile. Open <https://localhost:7233>. If your browser warns about the development certificate, trust it first with `dotnet dev-certs https --trust`.

Until you sign in, every page shows a **Sign in with GitHub** banner, and WebDevLoop stays in diagnostic-only mode. See [Health](health.md).

## Sign in

1. Click **Sign in with GitHub** in the banner, or open the [GitHub](github.md) page.
2. Authorize the App on GitHub. You come back to WebDevLoop.
3. Open [Health](health.md). All checks should say **Passed** and the readiness mode should say **Operational**. If a check fails, the page shows a **Fix** hint.

## Register your first repository

1. Open [Repositories](repositories.md) and click **Add repository**.
2. Pick a repository from the list. It lists the repositories the GitHub App can reach. Use the filter if the list is long.
3. Click **Add**. The repository is added and enabled. Its base branch is `main` unless you change it with **Edit** or in [Settings](settings.md).
4. Select it in the repository box in the sidebar (it is selected automatically if it is the first).

If your repository is missing from the list, add it to the App installation on the [GitHub](github.md) page.

## Queue your first spec

A spec is a GitHub issue that has open sub-issues. Each sub-issue is one ticket. Tickets can block each other with "blocked by" links.

1. Open [Queue](queue.md).
2. Type the issue number (`123`, `#123`) or the issue URL into **Queue parent spec issue**.
3. Click **Queue**.
4. The run appears in the list. Open **Details** to follow it. See [Spec runs](runs.md).

By default one spec per repository runs at a time. More specs wait in the queue.

## What next

- Learn the screens: [Dashboard](dashboard.md), [Queue](queue.md), [Spec runs](runs.md).
- If a run stops, read [Needs attention](needs-attention.md).
- Tune models and limits in [Settings](settings.md).
