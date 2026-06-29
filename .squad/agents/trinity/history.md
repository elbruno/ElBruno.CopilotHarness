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


## Session: 2026-06-29T16-36-31 — Live View Rule vs. Intent Clarity
- **Outcome:** Complete
- **Decision:** 2026-06-29T20-33-01 recorded
- Fixed misleading intent pill by making matched rule the dominant signal (blue, "Matched rule" label).
- Processor stage now shows decision source: "🧠 Decided by local model" or "⚙️ Keyword/heuristic fallback".
- Intent rendered as muted secondary hint with "(guess)" qualifier.
- Broadened "GitHub actions" starter rule to read-only repo/issue/PR questions.
- Documented intent-vs-rule distinction in docs/Rules_Engine.md.
- Admin.Web builds clean (0 warnings).
