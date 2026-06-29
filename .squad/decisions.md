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

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
