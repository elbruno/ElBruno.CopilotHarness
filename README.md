# ElBruno.CopilotHarness

BYOK harness for GitHub Copilot built with .NET 10 and .NET Aspire.

## Current projects

- `src/ElBruno.CopilotHarness.AppHost`
- `src/ElBruno.CopilotHarness.ServiceDefaults`
- `src/ElBruno.CopilotHarness.Router.Api`

## Phase 1 router behavior

- OpenAI-compatible `POST /v1/chat/completions`
- 3 logical model profiles (`local`, `small`, `big`) configured in `Routing`
- Basic deterministic routing rules (explicit profile, system message, prompt size, streaming, default fallback)
- Explainability headers on each completion response:
  - `x-harness-model-profile`
  - `x-harness-model-deployment`
  - `x-harness-routing-reason`
- Health endpoints:
  - `/health` (self + Foundry endpoint readiness probe)
  - `/alive` (liveness only)

## Local run

Configure secrets (AppHost user-secrets):

```powershell
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryEndpoint https://<your-foundry-endpoint>
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryApiKey <your-api-key>
```

Then run:

```powershell
dotnet run --project .\src\ElBruno.CopilotHarness.AppHost
```

## Phase 1 automated tests

`tests/ElBruno.CopilotHarness.Router.Api.Tests` covers current Phase 1 behavior:

- basic deterministic routing decisions
- explainability headers on `/v1/chat/completions`
- `/health` and `/alive` smoke checks
- streaming passthrough smoke (`text/event-stream`)

Run:

```powershell
dotnet test .\ElBruno.CopilotHarness.slnx
```
