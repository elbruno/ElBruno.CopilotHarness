# Project Context

- **Owner:** Bruno Capuano
- **Project:** ElBruno.CopilotHarness
- **Description:** Intelligent BYOK harness for GitHub Copilot with policy-based routing, explainability, benchmarking, and continuous evaluation.
- **Stack:** .NET 10, ASP.NET Core, Blazor, .NET Aspire, Microsoft Agent Framework, Microsoft.Extensions.AI, EF Core, SQLite
- **Created:** 2026-06-26

## Learnings

- Initialized as frontend owner for the Blazor Admin experience.


## Session: 2026-06-29T15-28-35 — Live Routing & Models UX
- **Outcome:** Complete
- **Decision:** 2026-06-29T19-21-23 recorded
- LiveRouting: filter bar, client-side paging, per-row/bulk/clear-all deletes (two-step confirms).
- Models: editor moved to Rules-style modal with delete confirm.
- AdminApiClient: DeleteTraceAsync, DeleteTracesAsync, ClearTracesAsync.
- CSS: .page-header token, .empty-state, .confirm-banner, shared styles.
- Feed limit 50→200. Admin.Web builds clean.
