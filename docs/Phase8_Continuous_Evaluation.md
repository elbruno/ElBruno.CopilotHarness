# Phase 8 – Continuous Evaluation UI & Backend

Last updated: 2026-06-27

## Overview

Phase 8 adds continuous evaluation capabilities to the Copilot Harness Admin UI and backend. The Recommendation Agent monitors routing behaviour, suggests rule changes, and surfaces confidence data — all requiring human approval before taking effect.

Shadow routing silently replicates production traffic to secondary model profiles, accumulates rule confidence scores, and feeds the continuous benchmark scheduler. Recommendations are reviewed by operators through the approval workflow UI before any routing change takes effect.

## Backend services

### Shadow Routing (`Router.Api`)

`IShadowRoutingService` / `ShadowRoutingService` is registered as a scoped service and called fire-and-forget from the `/v1/chat/completions` endpoint after the primary response is returned to the caller.

Flow:
1. Reads the shadow config from `IShadowRoutingStore` (sampling rate, shadow profile)
2. Applies sampling — requests are skipped probabilistically to limit load
3. Calls the shadow upstream with the same prompt
4. Records the shadow result via `IShadowRoutingStore.RecordAsync`
5. Updates rule confidence scores via `IRuleConfidenceStore.RecordAsync`

Config is stored in the same SQLite database as the primary routing config (key `__config__` in the `ShadowRequests` table).

### Recommendation Agent — Router.Api

`IRecommendationAgent` / `DeterministicRecommendationAgent` in `Router.Api/Intelligence/RecommendationAgent.cs` analyses rule confidence scores and generates `RecommendationResult` records. Used by the Phase 8 admin endpoints (`/admin/phase8/recommendations`).

### Recommendation Agent — Judge.Web

`IRecommendationAgent` / `RecommendationAgent` in `Judge.Web/RecommendationAgent.cs` analyses completed benchmark run results and generates `RecommendationEntity` records stored in the Judge SQLite database.

Logic:
- Collects all completed benchmark runs from the last 30 days
- Groups results by profile; computes average score per profile
- Identifies the best-performing profile
- If score difference vs current is > 0.10 points and no pending recommendation exists for that profile, creates a new `Pending` recommendation

### Continuous Benchmark Scheduler (`Judge.Web`)

`ContinuousBenchmarkScheduler` is a `BackgroundService` (hosted service) that:
- Runs on a configurable interval (default 60 minutes, `ContinuousBenchmarkOptions.IntervalMinutes`)
- Selects a random 25 % sample of stored prompts for each benchmark cycle
- Creates benchmark runs for each active routing profile
- Calls the AI Judge for scoring each completion
- Stores results via `JudgeDbContext.BenchmarkRuns` / `BenchmarkItems`
- After each cycle triggers `IRecommendationAgent.AnalyzeAsync` to refresh recommendations

Registered as:
```csharp
builder.Services.AddSingleton<ContinuousBenchmarkScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ContinuousBenchmarkScheduler>());
```

The singleton lifetime lets the `/judge/continuous-eval/schedule` endpoint inject the same instance to report scheduler state.

### Rule Confidence Scoring

`IRuleConfidenceStore` / `RuleConfidenceStore` in `Router.Core/Persistence/Phase8Stores.cs` maintains a rolling 7-day window of per-rule confidence scores. Each shadow routing call that triggers a rule records a success or failure based on whether the shadow response matched the primary.

Confidence is surfaced via:
- `GET /admin/phase8/rules/{ruleKey}/confidence` — Router.Api admin endpoint
- `GET /admin/rules/confidence` — Admin.Web API client aggregated view

## Continuous Eval API (Judge.Web)

| Method | Path | Purpose |
|---|---|---|
| GET | `/judge/continuous-eval/recommendations` | List all recommendations |
| POST | `/judge/continuous-eval/recommendations/analyze` | Run analysis and return new recommendations |
| PUT | `/judge/continuous-eval/recommendations/{id}/review` | Approve or reject a recommendation |
| GET | `/judge/continuous-eval/schedule` | Scheduler state (IsRunning, LastRunAt, NextRunAt) |

### Review request body
```json
{ "status": "Approved", "reviewNotes": "Looks good" }
```

Valid status values: `Approved`, `Rejected`

## Phase 8 admin API (Router.Api)

| Method | Path | Purpose |
|---|---|---|
| GET | `/admin/phase8/shadow/config` | Get shadow routing config |
| PUT | `/admin/phase8/shadow/config` | Update shadow config (sampling rate, shadow profile) |
| GET | `/admin/phase8/shadow/history` | Recent shadow requests |
| GET | `/admin/phase8/rules/{ruleKey}/confidence` | Rule confidence data |
| POST | `/admin/phase8/rules/{ruleKey}/confidence/record` | Record rule invocation |
| GET | `/admin/phase8/benchmarks` | List benchmark runs |
| POST | `/admin/phase8/benchmarks` | Create benchmark run |
| GET | `/admin/phase8/benchmarks/{runId}` | Get benchmark run details |
| GET | `/admin/phase8/recommendations` | List recommendations |
| POST | `/admin/phase8/recommendations/analyze` | Trigger analysis |
| PUT | `/admin/phase8/approvals/{approvalId}/decide` | Approve/reject routing change |
| GET | `/admin/phase8/teams` | List team profiles |
| PUT | `/admin/phase8/teams/{teamId}` | Upsert team profile |
| GET | `/admin/phase8/teams/{teamId}` | Get team profile |
| DELETE | `/admin/phase8/teams/{teamId}` | Delete team profile |
| GET | `/admin/phase8/projects` | List project profiles |
| PUT | `/admin/phase8/projects/{projectId}` | Upsert project profile |

## Admin UI pages

### `/approvals` — Rule Change Approvals

Displays rule change recommendations produced by the Recommendation Agent. Each recommendation shows:

- **Rule key** — which routing rule is affected
- **Current → Recommended** values
- **Confidence** — percentage with colour-coded pill and progress bar
- **Rationale** — explanation from the agent
- **Status** — Pending / Approved / Rejected

Operators can **Approve** or **Reject** (with an optional reason). No rule change takes effect without explicit human approval.

### `/profiles` — Team & Project Profiles

Manage two types of Phase 8 routing profiles:

| Type | Purpose |
|---|---|
| **Team profiles** | Named profile groups with preferred model lists and an optional default flag |
| **Project profiles** | Per-project overrides linked to a team profile, supporting tag-based routing |

### `/benchmarks` — Continuous Benchmarking

Shows the state of the continuous evaluation pipeline:

- Scheduler status, last run time, and next scheduled run
- A table of recent benchmark runs (status, trigger, test counts, pass/fail)
- Per-profile result cards with average latency, token usage, and AI Judge score

### `/rules` — Rules Editor (confidence indicators)

The existing Rules Editor fetches confidence data from `GET /admin/rules/confidence` and renders a **Rule confidence** section below the form when data is available. Each rule shows a confidence percentage pill (green ≥ 80 %, amber ≥ 50 %, red < 50 %) with trend label and last-evaluated timestamp.

## Design principles

- **Human approves rule changes** — core product principle enforced in both UI and API.
- Shadow routing is fire-and-forget; it never blocks the primary response.
- Scheduler cycles are jittered to avoid thundering herd.
- Empty states clearly distinguish "no data" from "backend not configured".
- SQLite `DateTimeOffset` columns are sorted in-memory after `ToListAsync` to avoid EF Core limitations.

## Evaluation.Worker (`ElBruno.CopilotHarness.Evaluation.Worker`)

A dedicated .NET Worker Service (`net10.0`) that runs alongside Router.Api in the Aspire AppHost. It connects to the same database and runs two hosted services:

### `ContinuousBenchmarkJob`
Polls every **5 minutes** for `BenchmarkRuns` in `pending` status, advances them through `running` → `completed` (or `failed`). Plug in real LLM judge calls to replace the stub.

### `RecommendationScheduler`
Runs every **1 hour**, fetches all rule confidence scores, and logs a warning for any rule whose `ConfidenceScore` is below 0.80. Extend this to call `IApprovalWorkflowStore.CreateAsync` to auto-generate approval requests when rules degrade.

### AppHost registration
```csharp
builder.AddProject<Projects.ElBruno_CopilotHarness_Evaluation_Worker>("evaluation-worker")
    .WithReference(routerDatabase)
    .WaitFor(routerApi);
```

