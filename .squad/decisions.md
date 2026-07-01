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

### 2026-06-30T21-12-00: Size-aware tool override + local max_tokens cap to resolve VS Code "Response too long"
**By:** Coordinator
**Summary:** Payload size tracking in tool-request routing. Prompts ≤12000 chars stay local; larger override to cloud tool-capable model with reason. Ollama responses capped at 4096 tokens default. Live-verified: large 168150-char payload → gpt-5-mini (cloud), small 53-char → llama3.1 (local). 159 tests pass.
**References:** commit 410a6e4, FindToolCapableModel.cs, OpenAiApiUtilities.cs, appsettings.json

### 2026-06-29T21-35-41: Local-first tool-calling override: llama3.1:8b confirmed as local tool-caller; Ollama preferred over Azure cloud in FindToolCapableModel
**By:** Scribe
**Summary:** Local-first tool-calling override: llama3.1:8b confirmed as local tool-caller; Ollama preferred over Azure cloud in FindToolCapableModel
**References:** commit: e6db8ff, RequestRoutingService.cs, SqliteRoutingStoreInitializer.cs, ToolCallingRoutingTests.cs, ToolGuardAndUpstreamOutcomeEndpointTests.cs, docs/Model_Registry.md, docs/Troubleshooting.md

Coordinator empirically tested installed ollama models for STREAMING structured tool_calls:
- **llama3.1:8b** ✅ Emits proper structured tool_calls with valid args (e.g. {"query":"is:open"})
- llama3.2:3b ✗ Gives empty args
- qwen2.5-coder ✗ Dumps call into content
- gpt-oss:20b ✗ Emits no tool_calls

Implementation: Seeded 'ollama llama3.1 (tools)' (llama3.1:8b, SupportsToolCalling=true, enabled, not processor) in SqliteRoutingStoreInitializer. Flipped OpenAiApiUtilities.FindToolCapableModel to prefer ModelProviderType.Ollama (local) over Azure cloud. Cloud is now fallback only. Updated tool-guard tests and docs/Model_Registry.md + docs/Troubleshooting.md. All 153 tests pass.

### 2026-07-01T01-10-58: Size-aware tool override + local max_tokens cap to resolve VS Code "Response too long" error
**By:** Coordinator
**Summary:** Size-aware tool override + local max_tokens cap to resolve VS Code "Response too long" error
**References:** commit 410a6e4, trace-8cda98c4, src/ElBruno.CopilotHarness.Router.Api/Routing/FindToolCapableModel.cs, src/ElBruno.CopilotHarness.Router.Api/Utilities/OpenAiApiUtilities.cs, appsettings.json

Root Cause: Large agentic Copilot payloads (146942 chars) were overridden to LOCAL tool-caller llama3.1:8b, which over-generated (27s, HTTP 200) and tripped VS Code's "Response too long" output cap.

Fix 1 (Size-Aware Override): Tool requests with total prompt <= Routing:Rules:LocalToolCallingMaxPromptCharacters (default 12000) stay local; larger payloads override to a CLOUD tool-capable model. FindToolCapableModel gained a preferLocal bool.

Fix 2 (Safety Net): OpenAiApiUtilities.ClampMaxTokens caps output tokens for Ollama routes at Routing:Rules:LocalRouteMaxTokens (default 4096).

Verification: Large 168150-char tool payload → foundry gpt-5-mini (cloud, 6.7s, 200); Small 53-char tool payload → ollama llama3.1 (tools) local with clean structured tool_calls. 159 tests pass.

### 2026-07-01T15-43-40: Hired Seraph as DevRel and Storytelling Engineer
**By:** Squad-Coordinator
**Summary:** Hired Seraph as DevRel and Storytelling Engineer
**References:** team.md, routing.md, casting/registry.json, .squad/agents/seraph/charter.md

Validated a gap: the team had engineering + DevX (Switch) coverage but no owner for promotion, storytelling, pitches, social copy, generated imagery (t2i), animated explainer slide decks, and narrative docs coherence. Hired Seraph (Matrix universe) to own this. Boundary vs Switch: Switch owns README/docs UX mechanics and onboarding; Seraph owns story, promotion, visual narrative, and accuracy/coherence review. Requested by Bruno Capuano.

### 2026-07-01T15-46-19: README screenshot reduction: 8 → 1 hero; gallery relocated to docs/screenshots.md
**By:** Switch
**Summary:** README screenshot reduction: 8 → 1 hero; gallery relocated to docs/screenshots.md
**References:** README.md, docs/screenshots.md, docs/images/admin-live-routing.png

Reduced README.md from 8 embedded screenshots to 1 hero shot. All 7 non-hero screenshots relocated to a new docs/screenshots.md gallery page; no image files were deleted from disk. Hero chosen: docs/images/admin-live-routing.png (directly communicates real-time routing decisions with prompt → model → rule → explanation chain). Removed entire Screenshots section and inline admin-setup-connect.png and byok-chatLanguageModels-json.png from README. Gallery created at docs/screenshots.md with all 8 screenshots for reference. README link added: "📸 More screenshots: docs/screenshots.md" directly below hero image.

### 2026-07-01T15-51-14: Promotional bundle + animated slide deck created for ElBruno.CopilotHarness (first Seraph assignment)
**By:** Seraph
**Summary:** Promotional bundle + animated slide deck created for ElBruno.CopilotHarness
**References:** docs/presentation/harness-layers.html, docs/promo/{blog-post.md,pitch-5min.md,linkedin.md,twitter.md}, docs/assets/IMAGE-PROMPTS.md, docs/promo/COHERENCE-REVIEW.md

Created full promotional and presentation bundle: docs/presentation/harness-layers.html (5-slide self-contained animated HTML, dark GitHub theme, keyboard + click navigation, no CDN dependencies); docs/promo/blog-post.md (~1100 words technical blog); docs/promo/pitch-5min.md (5-minute video script with timestamps and shot list); docs/promo/linkedin.md (professional post with emojis, hashtags); docs/promo/twitter.md (8-tweet X/Twitter thread).

docs/assets/IMAGE-PROMPTS.md staged two prompts for t2i (t2i installed but NOT configured—0/3 providers): social-card.png (1200×630) and hero-bg.png (1600×900) with ready-to-run commands.

docs/promo/COHERENCE-REVIEW.md: Narrative/accuracy review flagged screenshot audit (recommend cutting README from 7 shots to 2), 3 top findings (screenshot bloat, missing 5-layer model in README, stale test badge saying 58 vs actual 152+). All content verified against AppHost.cs, Architecture.md, Live_Routing.md, Model_Registry.md, README.md, and decisions.md. No invented features.
