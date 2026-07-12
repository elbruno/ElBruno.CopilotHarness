# Current Progress

Last updated: 2026-07-01

## Status

- Phase 0: complete
- Phase 1: complete
- Phase 2: complete
- Phase 3: complete
- Phase 4: complete
- Phase 5: core complete
- Phase 6: backend foundations in place
- Phase 7: VS Code Extension
- Phase 8: complete (UI + backend plumbing)

## Phase 8 backend - what was implemented

- **Shadow routing pipeline** - `IShadowRoutingStore` / `ShadowRoutingStore`: config persistence and request log storage. Endpoints: `GET/PUT /admin/phase8/shadow/config`, `POST/GET /admin/phase8/shadow/results`.
- **Rule confidence scoring** - `IRuleConfidenceStore` / `RuleConfidenceStore`: upsert/list confidence windows per rule key. Endpoints under `/admin/phase8/rules/confidence`.
- **Continuous benchmarking** - `IBenchmarkStore` / `BenchmarkStore`: benchmark runs and per-item results. Endpoints under `/admin/phase8/benchmarks/`.
- **Human approval workflow** - `IApprovalWorkflowStore` / `ApprovalWorkflowStore`: pending/decided approvals, decision endpoint. Endpoints under `/admin/phase8/approvals/`.
- **Team and project profiles** - `ITeamProjectProfileStore` / `TeamProjectProfileStore`: upsert/get/list/delete for both entity types. Endpoints under `/admin/phase8/profiles/`.

All 7 new EF Core entities, migration `202606270003_AddPhase8ContinuousEvaluation`, and idempotent `CREATE TABLE IF NOT EXISTS` bootstrap added.

`Evaluation.Worker` background service added: `ContinuousBenchmarkJob` (5-min poll) and `RecommendationScheduler` (1-hour rule-confidence scan). Registered in AppHost.

Test coverage: 12 new integration tests (`Phase8ContinuousEvaluationTests`). Full suite: **58/58 passing** (48 Router.Api + 10 Judge.Web).

## Current focus

- Keep the router stable and compatible with BYOK clients
- Maintain the judge app and benchmark/reporting contract
- Preserve phase boundaries and keep Judge separate from Router and Admin

## Foundry Local Processor Plan B — Phases A/B/C/D (complete)

Full implementation replacing Ollama as the default prerequisite for the routing rules engine.
All 4 phases merged on branch `feature/model-status-and-ab-eval`.

### Phase A — Model Status UI + Setup Guidance
- `GET /admin/models/{id}/status` endpoint — probes connectivity for every provider type
- Setup page cards: prerequisites, status badges (✅ / ⚠️ / ❌), guided setup steps per provider
- Foundry Local card shows download commands and current endpoint health

### Phase B — Shadow Processor A/B Evaluation
- `IsShadowProcessor` flag on `ModelConnectionEntity` — any model can run as a silent shadow
- Shadow runs in parallel to the primary processor; result stored on `ClassificationResult.ShadowResult`
- Context facts added: `shadow.intent`, `shadow.processorModel`, `shadow.confidence`, `shadow.agreement`
- Live Routing UI: shadow badge (agree/disagree) beside primary processor stage
- `GET /admin/benchmarks/ab-classifier` — aggregated agreement statistics per intent pair

### Phase C — Foundry Local SDK Service + Catalog UI
- Added `Microsoft.AI.Foundry.Local` NuGet package (v1.2.3)
- `FoundryLocalSdkService` singleton: starts the SDK's embedded web server, caches the bound URL
- 5 new Admin endpoints: `/admin/foundry-local/status`, `init`, `catalog`, `download`, `progress`
- Admin.Web "Foundry Local" page: SDK status badge, model table (cached/loaded pills, download progress bars), setup guidance card
- Test coverage: +4 endpoint tests

### Phase D — FoundryLocalSdk Zero-Config Provider Type
- `ModelProviderType.FoundryLocalSdk = 3` — new provider type; included in `IsLocalProvider()`
- `FoundryLocalSdkChatCompletionsProvider`: auto-discovers SDK web service URL from `FoundryLocalSdkService.WebServiceUrl`, no endpoint config needed
- Seed entry `seed-foundry-local-sdk-phi4mini` (type `FoundryLocalSdk`, `IsProcessor=false` — user promotes)
- Admin UI: `foundry-local-sdk` type dropdown option, purple badge in Live Routing
- Test coverage: +5 provider type tests

**Test totals: 220/220 passing** (was 215 before Phase D, 199 before Plan B)

## Next planned phase

- Phase 9 (if defined) — see `docs/PRD.md`

## Key links

- `README.md`
- `docs/User_Manual.md`
- `docs/Docs_Index.md`
- `docs/PRD.md`
- `docs/Phase8_Continuous_Evaluation.md`
