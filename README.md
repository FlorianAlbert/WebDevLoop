# WebDevLoop

WebDevLoop is a local ASP.NET Core Blazor Server app for orchestrating GitHub issue implementation workflows.

## Build and test

Requires the .NET 11 SDK (this repo pins `11.0.100-rc.1.26425.128` in `global.json`).

```bash
dotnet build WebDevLoop.slnx
dotnet test
```

The test projects use xUnit v3 with Microsoft.Testing.Platform configured in `global.json`.
