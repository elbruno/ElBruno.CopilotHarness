# Project Context

- **Owner:** Bruno Capuano
- **Project:** ElBruno.CopilotHarness
- **Description:** Intelligent BYOK harness for GitHub Copilot with policy-based routing, explainability, benchmarking, and continuous evaluation.
- **Stack:** .NET 10, ASP.NET Core, Blazor, .NET Aspire, Microsoft Agent Framework, Microsoft.Extensions.AI, EF Core, SQLite
- **Created:** 2026-06-26

## Learnings

- Initialized as backend owner for Router API and core service integration.


## Session: 2026-06-29T15-28-35 — Trace Deletion Endpoints & Tests
- **Outcome:** Complete
- **Decision:** 2026-06-29T19-15-39 recorded
- Delivered DELETE /admin/traces/{id}, POST /admin/traces/delete, DELETE /admin/traces (all idempotent 200 OK).
- Extended IExecutionTraceStore: Remove(id), RemoveMany(ids), Clear().
- Implementations: InMemoryExecutionTraceStore, PersistentExecutionTraceStore (EF Core).
- Raised GET /admin/telemetry/requests limit 50→200.
- Router.Api builds clean; 132 tests pass (3 new trace-deletion tests by Link).
