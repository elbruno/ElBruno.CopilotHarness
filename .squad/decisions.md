# Squad Decisions

## Active Decisions

### 2026-06-27T13-48-14: Phase 1 Router API tests use in-memory upstream stubs
**By:** Neo
**Summary:** Implemented Phase 1 automated coverage with WebApplicationFactory-based xUnit tests and stubbed HttpMessageHandler for foundry-health and FoundryChatCompletionsClient.
**References:** tests/ElBruno.CopilotHarness.Router.Api.Tests/RouterApiWebApplicationFactory.cs, RouterApiEndpointsTests.cs, BasicModelRouterTests.cs

### 2026-06-27T15-48-35: Phase 8 UI implemented in Admin.Web — approvals, profiles, benchmarks, rule confidence
**By:** Trinity
**Summary:** Implemented Phase 8 UI-only features (ApprovalWorkflow, Profiles, Benchmarks, rule confidence visualization) with silent empty-state fallback when backend unavailable.
**References:** src/ElBruno.CopilotHarness.Admin.Web/Components/Pages/{ApprovalWorkflow.razor,Profiles.razor,Benchmarks.razor}, Rules.razor, AdminContracts.cs, AdminApiClient.cs

### 2026-06-29T19-14-51: Website Improvement Direction for Admin.Web — harmonize design tokens, extract Rules-page patterns, low-risk page-by-page polish
**By:** Morpheus
**Summary:** Prioritized direction to harmonize Admin.Web design tokens, promote Rules-page CSS patterns site-wide, add shared page-header and component standards (badges, chips, modals, tables, skeletons), low-risk P1/P2/P3 punch list. Advice only; no breaking changes or behavioral rewrites.
**References:** src/ElBruno.CopilotHarness.Admin.Web/wwwroot/app.css, Components/Pages/Rules.razor, Components/Pages/Rules.razor.css, Components/Layout/MainLayout.razor

### 2026-06-29T19-15-39: Trace-delete endpoints: DELETE returns 200 with {deleted}, not 404
**By:** Neo
**Summary:** Added backend trace deletion support (single, bulk, clear-all) with idempotent 200-OK responses and deleted flags. Extended IExecutionTraceStore with Remove/RemoveMany/Clear. Raised GET /admin/telemetry/requests default limit 50→200.
**References:** src/ElBruno.CopilotHarness.Router.Api/Intelligence/RoutingWorkflow.cs, AdminEndpoints.cs, AdminContracts.cs

### 2026-06-29T19-21-23: Live Routing & Models UX: client-side filter/paging + two-step confirm deletes, Models editor moved to Rules-style modal
**By:** Trinity
**Summary:** Implemented LiveRouting (filter bar, client-side paging, per-row delete, bulk delete, clear-all with two-step confirms); moved Models editor to Rules-style modal. Added AdminApiClient delete methods and shared CSS polish (page-header, empty-state, confirm-banner). Feed limit 50→200.
**References:** src/ElBruno.CopilotHarness.Admin.Web/Components/Pages/{LiveRouting.razor,Models.razor}, Models.razor.css, AdminApiClient.cs, AdminContracts.cs, wwwroot/app.css

### 2026-06-29T20-33-01: Live view now leads with the matched rule + decision source; classifier intent demoted to a muted secondary hint
**By:** Trinity
**Summary:** Fixed misleading Live-view intent pill. Matched rule is now the dominant signal (blue highlight, labeled "Matched rule"). Processor stage shows decision source ("🧠 Decided by local model" or "⚙️ Keyword/heuristic fallback"). Intent rendered as muted secondary hint with "(guess)" qualifier when model-decided. Top summary cards clarified to show "By classifier intent (heuristic)". Broadened default "GitHub actions" starter rule to cover read-only repo/issue/PR questions. Documented all starter rules + intent-vs-rule explainer in docs/Rules_Engine.md.
**References:** src/ElBruno.CopilotHarness.Admin.Web/Components/Pages/LiveRouting.razor, LiveRouting.razor.css, Rules.razor, docs/Rules_Engine.md, AdminContracts.cs

### 2026-06-29T20-57-06: Post-call upstream/tool facts patched onto stored routing trace
**By:** Neo
**Summary:** Post-call upstream/tool facts are patched onto the stored routing trace via a new IExecutionTraceStore.AppendFacts(traceId, facts). Implemented in both InMemoryExecutionTraceStore and PersistentExecutionTraceStore. Tool-capability guard prefers enabled AzureOpenAI tool-capable models; fallback returns 502/504 OpenAI-style errors.
**References:** src/ElBruno.CopilotHarness.Router.Api/Intelligence/RoutingWorkflow.cs, AdminEndpoints.cs, OpenAiApiUtilities.cs, PersistentExecutionTraceStore.cs

### 2026-06-29T20-57-07: Tool-calling capability + upstream-outcome observability in Admin.Web
**By:** Trinity
**Summary:** Frontend mirrored tool-calling observability. Live Routing cards show upstream-outcome row (success/fail badge, latency, tools chip, override callout). Models page gained SupportsToolCalling toggle and 🛠 capability chip. Errors-only filter in Live view. Docs updated: Troubleshooting, Live_Routing, Model_Registry, Logging (new), Docs_Index.
**References:** src/ElBruno.CopilotHarness.Admin.Web/Components/Pages/{LiveRouting.razor,Models.razor}, AdminContracts.cs, docs/

### 2026-06-29T20-57-08: Tests for SupportsToolCalling guard + upstream outcome capture
**By:** Link (Tester)
**Summary:** Added 20 unit and endpoint tests covering SupportsToolCalling flag, tool-calling guard, upstream outcome capture, and feed DTO fields. Tests added to ToolCallingRoutingTests.cs and ToolGuardAndUpstreamOutcomeEndpointTests.cs. No production code modified. All 152 tests passing.
**References:** tests/ElBruno.CopilotHarness.Router.Api.Tests/ToolCallingRoutingTests.cs, ToolGuardAndUpstreamOutcomeEndpointTests.cs, RouterApiWebApplicationFactory.cs

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
