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
