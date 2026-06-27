# ElBruno.CopilotHarness

BYOK harness for GitHub Copilot built with .NET 10 and .NET Aspire.

## What it is

An intelligent BYOK harness for GitHub Copilot. The repo currently includes:

- Aspire AppHost
- Router API
- Admin Web
- Judge Web
- SQLite + EF Core admin storage
- Phase 0-7 routing, intelligence, judge, multi-client telemetry, and VS Code extension contracts

## Current projects

- `src/ElBruno.CopilotHarness.AppHost`
- `src/ElBruno.CopilotHarness.ServiceDefaults`
- `src/ElBruno.CopilotHarness.Router.Core`
- `src/ElBruno.CopilotHarness.Router.Api`
- `src/ElBruno.CopilotHarness.Admin.Web`
- `src/ElBruno.CopilotHarness.Judge.Web`
- `src/ElBruno.CopilotHarness.VSCode`

## Quick start

### Prerequisites

- .NET 10 SDK
- Aspire tooling

### Configure secrets

```powershell
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryEndpoint https://<your-foundry-endpoint>
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryApiKey <your-api-key>
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:AdminApiKey <optional-admin-bearer-token>
```

### Run

```powershell
dotnet run --project .\src\ElBruno.CopilotHarness.AppHost
```

### VS Code extension

```powershell
cd .\src\ElBruno.CopilotHarness.VSCode
npm install
npm run compile
```

Open the folder in VS Code and press `F5` to run the extension host.

## Docs

- `docs/Docs_Index.md`
- `docs/Current_Progress.md`
- `docs/User_Manual.md`
- `docs/Architecture.md`
- `docs/API_Reference.md`
- `docs/Troubleshooting.md`
- `docs/Runbook.md`
- `docs/Contributing.md`
- `docs/Phase2_Admin.md`
- `docs/Phase3_Harness_Intelligence.md`
- `docs/Phase4_Client_Compatibility.md`
- `docs/Phase4_MultiClient_Dashboard.md`
- `docs/Phase5_AI_Judge.md`
- `docs/Phase7_VSCode_Extension.md`

## Validate

```powershell
dotnet test .\ElBruno.CopilotHarness.slnx
```
