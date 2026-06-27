# User Manual

## What this app does

ElBruno.CopilotHarness is a BYOK harness for GitHub Copilot with:

- OpenAI-compatible router endpoints
- Aspire-based local orchestration
- Admin UI for routing, models, rules, and validation
- Multi-client telemetry for VS Code, Copilot CLI, and Copilot App
- Judge app for prompt replay, multi-model benchmarks, and evaluation reports

## Install and run

### Prerequisites

- .NET 10 SDK
- Aspire tooling

### Configure secrets

```powershell
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryEndpoint https://<your-foundry-endpoint>
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryApiKey <your-api-key>
```

### Start

```powershell
dotnet run --project .\src\ElBruno.CopilotHarness.AppHost
```

## Common URLs

- Router: `/v1/chat/completions`
- Router models: `/v1/models`
- Router responses: `/v1/responses`
- Health: `/health`
- Liveness: `/alive`
- Admin dashboard: `/`
- Setup wizard: `/setup`
- Models: `/models`
- Rules: `/rules`
- Playground: `/playground`
- Validation: `/validation`
- Judge app root: open `judge-web` from the Aspire dashboard
- Judge replay prompts: `/replay`
- Judge benchmarks: `/benchmarks`
- Judge reports: `/reports`
- Judge manual controls: `/manual`
- Judge manual benchmark: `/judge/benchmarks/manual`
- Judge reports: `/judge/benchmarks/runs` and `/judge/reports/{runId}`

## What to look at first

1. Open `README.md` for the quickest overview.
2. Open `docs/Current_Progress.md` to see what phase is done.
3. Open `docs/Docs_Index.md` to find the rest of the docs.
