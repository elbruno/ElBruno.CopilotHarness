# User Manual

## What this app does

ElBruno.CopilotHarness is a BYOK harness for GitHub Copilot with:

- OpenAI-compatible router endpoints
- Aspire-based local orchestration
- Admin UI for routing, models, rules, and validation
- Multi-client telemetry for VS Code, Copilot CLI, and Copilot App
- Judge app for prompt replay, multi-model benchmarks, and evaluation reports
- VS Code extension for routing explanations and dashboard links

## Install and run

### Prerequisites

- .NET 10 SDK
- Aspire CLI (`aspire`)

### Configure Aspire parameters (one-time)

Run these commands **once** inside the AppHost project folder before the first `aspire run`.
Aspire saves them locally so you are never prompted again.

```powershell
cd src/ElBruno.CopilotHarness.AppHost

aspire secret set FoundryEndpoint "https://<your-resource>.openai.azure.com/openai/v1"
aspire secret set FoundryApiKey   "<your-azure-foundry-api-key>"
aspire secret set AdminApiKey     "<any-password-you-choose>"
```

| Parameter | What it is |
|---|---|
| `FoundryEndpoint` | Azure AI Foundry base URL (ends in `/openai/v1`) |
| `FoundryApiKey` | Azure AI Foundry API key |
| `AdminApiKey` | A password **you invent** — protects the admin endpoints. Any string works. |

### Start

```powershell
aspire run
```

## VS Code extension

1. Open `src/ElBruno.CopilotHarness.VSCode` in VS Code.
2. Run `npm install`.
3. Press `F5`.

### Commands

- `Harness: Show Status Panel`
- `Harness: Explain Routing`
- `Harness: Open Dashboard`
- `Harness: Open Trace`

### Chat

Use `@harness` in Copilot Chat to ask for routing explanations or dashboard links.

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
- Phase 7 extension docs: `docs/Phase7_VSCode_Extension.md`

## What to look at first

1. Open `README.md` for the quickest overview.
2. Open `docs/Current_Progress.md` to see what phase is done.
3. Open `docs/Docs_Index.md` to find the rest of the docs.
