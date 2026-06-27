# ElBruno.CopilotHarness

BYOK harness for GitHub Copilot built with .NET 10 and .NET Aspire.

## Current projects

- `src/ElBruno.CopilotHarness.AppHost`
- `src/ElBruno.CopilotHarness.ServiceDefaults`
- `src/ElBruno.CopilotHarness.Router.Core`
- `src/ElBruno.CopilotHarness.Router.Api`
- `src/ElBruno.CopilotHarness.Admin.Web`
- `src/ElBruno.CopilotHarness.Admin.Web`

## Phase 1 router behavior

- OpenAI-compatible `POST /v1/chat/completions`
- OpenAI-compatible `GET /v1/models` (logical profiles backed by configured deployments)
- OpenAI-compatible `POST /v1/responses` (minimal compatibility layer over current chat-completions routing/forwarding)
- OpenAI-style error envelope for invalid `POST /v1/chat/completions` payloads (`error.message`, `error.type`, `error.param`, `error.code`)
- OpenAI-style error envelope for invalid `POST /v1/responses` payloads
- 3 logical model profiles (`local`, `small`, `big`) configured in `Routing`
- Basic deterministic routing rules (explicit profile, system message, prompt size, streaming, default fallback)
- Explainability headers on each completion response:
  - `x-harness-model-profile`
  - `x-harness-model-deployment`
  - `x-harness-routing-reason`
- Health endpoints:
  - `/health` (self + Foundry endpoint readiness probe)
  - `/alive` (liveness only)

## Phase 2 backend/data additions

- SQLite + EF Core persistence is implemented in `Router.Core` (`HarnessDbContext`)
- Local-first DB path: `Persistence:DatabasePath` (default `App_Data\copilotharness-admin.db`)
- Runtime startup applies EF Core migrations and seeds model registry/rules/setup state from `Routing` config
- Admin API wiring now includes:
  - `/admin/setup/*`
  - `/admin/models`
  - `/admin/rules/basic`
  - `/admin/playground/evaluate`
  - `/admin/system/validation`
- Router `/v1/chat/completions` remains OpenAI-compatible and now reads routing config from persisted store

## Phase 3 intelligence additions

- A Phase 3 routing workflow has been added in `Router.Api` with Microsoft Agent Framework-compatible orchestration primitives.
- Routing now executes a deterministic intelligence pipeline:
  - context providers (`requested-model`, `streaming`, `prompt-shape`)
  - classification agent
  - rule advisor agent
  - final deterministic routing decision (preserving Phase 1/2 behavior)
- Each routed request includes an execution trace id header:
  - `x-harness-trace-id`
- Admin API can inspect traces:
  - `GET /admin/traces/{traceId}`

See `docs/Phase3_Harness_Intelligence.md` for details.

## Phase 4 multi-client dashboard additions

- Router telemetry now captures client metadata and request endpoint for routed requests.
- Admin API dashboard endpoint:
  - `GET /admin/dashboard/snapshot`
- Admin Web dashboard (`/`) now shows:
  - connected clients for VS Code / Copilot CLI / Copilot App
  - live routed requests (active + recent) with endpoint + selected profile + trace id

See `docs/Phase4_MultiClient_Dashboard.md` and `docs/Phase4_Client_Compatibility.md` for details.

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

Phase 2 admin UI is available from the Aspire dashboard as `admin-web`, and directly from:

- `http://localhost:<admin-port>/` (Dashboard)
- `/setup` (Setup Wizard)
- `/models` (Model Registry)
- `/rules` (Rules Editor + “Generate my first rules”)
- `/playground` (Routing Playground)
- `/validation` (System Validation)
- `/` now includes the Phase 4 telemetry dashboard (connected clients + live requests)

See `docs/Phase2_Admin.md` for details.

## Phase 1 automated tests

`tests/ElBruno.CopilotHarness.Router.Api.Tests` covers current Phase 1 behavior:

- basic deterministic routing decisions
- explainability headers on `/v1/chat/completions`
- `/v1/models` listing compatibility shape
- `/v1/responses` minimal compatibility contract + error envelope
- `/health` and `/alive` smoke checks
- streaming passthrough smoke (`text/event-stream`)

Run:

```powershell
dotnet test .\ElBruno.CopilotHarness.slnx
```
