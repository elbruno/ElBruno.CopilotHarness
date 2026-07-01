# Docs Coherence & Accuracy Review
## Prepared by Seraph (DevRel & Storytelling Engineer)
## Date: 2026-07-01

> **Scope:** Narrative accuracy, redundancy, story consistency, and screenshot bloat across `README.md` and `docs/`.  
> **Not in scope:** Mechanical README restructuring (Switch's domain) or code changes.  
> **Action owner:** Switch for structural edits; Bruno for factual sign-off.

---

## 1. README Screenshot Audit — Recommendation: Cut to ONE hero shot

The README currently shows **7 screenshots** (excluding the BYOK JSON example):

| # | Screenshot | Keep / Cut? | Rationale |
|---|---|---|---|
| 1 | `aspire-dashboard.png` — all services healthy | ✅ **KEEP as hero** | Immediately proves "this thing runs" to a new visitor. Shows the Aspire stack at a glance. Most credible first impression. |
| 2 | `admin-dashboard.png` — routing dashboard | Cut (fold into User_Manual) | The Live Routing shot is more impressive and tells the same story better. |
| 3 | `admin-live-routing.png` — prompt → model → rule → explanation | ⭐ **KEEP as strong #2** | This is the money shot — it *shows* the explainability value prop in one frame. If keeping two screenshots, this is the second one. |
| 4 | `admin-rules.png` — rules editor | Cut (move to Rules_Engine.md or User_Manual) | Good contextual doc screenshot, wrong place in README. |
| 5 | `admin-models.png` — model registry | Cut (Model_Registry.md already exists) | Duplicates doc already in Model_Registry.md. |
| 6 | `judge-benchmarks.png` — benchmark results | Cut (move to Phase5_AI_Judge doc or User_Manual) | Judge is a secondary feature for README overview; too much detail at this level. |
| 7 | `admin-setup-connect.png` — BYOK setup wizard | Keep *in the setup section only* | Contextually useful inside the BYOK setup steps. Not a hero shot — already correctly placed in a step-numbered list. |
| 8 | `byok-chatLanguageModels-json.png` — JSON example | Keep *in the setup section only* | Confirms what the JSON looks like; contextually correct placement. |

**Recommendation for Switch:** Move to ONE hero (aspire dashboard, full-width) + ONE value-prop shot (live routing, inline after the feature table). Everything else goes into the relevant doc pages. The README becomes sharply scannable instead of a screenshot gallery.

---

## 2. Factual Claims Verified Against Code/Docs

### ✅ Verified claims (safe to use in promo)

| Claim | Source |
|---|---|
| `aspire run` launches Router.Api + Admin.Web + Aspire dashboard | `AppHost.cs` — confirmed Router.Api, Admin.Web wired up |
| Default local model: Ollama `llama3.1:8b` | `Model_Registry.md` seeded examples table |
| Default cloud model: Azure AI Foundry `gpt-5-mini` | `Model_Registry.md` seeded examples table |
| BYOK custom endpoint: `http://localhost:5117/v1/chat/completions` | `README.md` step 5 |
| Routing decisions logged with plain-language explanation | `Live_Routing.md` — "Why" field in feed |
| AI classifier reads first ~200 chars of prompt | `Model_Registry.md` — Processor model section |
| API keys encrypted at rest (ASP.NET Core Data Protection) | `Model_Registry.md` — API-key encryption section |
| Size-aware tool guard routes large payloads to cloud | `Model_Registry.md`, `decisions.md` commit 410a6e4 |
| Routing footer injected into Copilot chat (demo toggle) | `Live_Routing.md` — "Routing footer" section |
| Tests passing (152 tests per decisions.md) | `decisions.md` — Link's decision 2026-06-29T20-57-08 |
| Stack: .NET 10, ASP.NET Core, Blazor, EF Core, SQLite, Microsoft.Extensions.AI | `Architecture.md`, `README.md` |

### ⚠️ Claims to soften / verify before publishing

| Claim | Issue | Recommendation |
|---|---|---|
| README badge says "58 tests passing" | `decisions.md` records 152 tests passing as of 2026-06-29. Badge appears stale. | Switch: update badge to current test count. Seraph: promotional content uses "actively tested" not a specific number. |
| README says "VS Code extension" as a feature | Extension mentioned in `README.md` feature table and `docs/Phase7_VSCode_Extension.md`. Could not confirm final shipped state from code inspection. | Soften to "VS Code extension (in active development)" in promo unless extension is confirmed released. Blog post uses hedged language. |
| "AI Judge" feature description | Judge.Web is confirmed in `AppHost.cs` and `docs/Phase5_AI_Judge.md`. Functional scope not fully read. | OK to mention as an included feature; avoid claiming specific benchmark metrics unless tested. |

---

## 3. Story Consistency Findings

### Finding 1: The README "What it does" table and the docs tell two different stories

The feature table in README leads with technical capability names ("OpenAI-compatible router", "Condition-based routing rules"). The docs tell a richer story about *why those capabilities matter*. The gap is widest on the explainability angle — the Live Routing doc is excellent, but nothing in the README feature table says "you can see *why* every routing decision was made." Recommendation (Switch): add an "Explainability" row or reframe the existing "Admin dashboard" row.

### Finding 2: "BYOK" is used without definition on first use in README

The tagline says "Intelligent BYOK harness" but BYOK is never expanded in the opening paragraph. New visitors (non-enterprise developers) may not know it means Bring Your Own Key. Recommendation (Switch): add "(Bring Your Own Key)" on first use in README intro.

### Finding 3: The 5-layer mental model is not in the README

The canonical 5-layer explainer (Copilot → BYOK Config → Router → Model Selector → Models) is the clearest way to explain the project in 30 seconds. It doesn't appear in the README. Recommendation (Switch): add a one-paragraph or visual summary of the 5-layer model near the top of README, above the feature table. This is the anchor point for all promotional content.

### Finding 4: AppHost.cs reveals more services than README/Architecture suggest

`AppHost.cs` registers **four services**: `router-api`, `evaluation-worker`, `judge-web`, and `admin-web` — plus optional PostgreSQL and Redis with `UseContainers=true`. The README and Architecture.md mention only "Router.Api, Admin.Web, and the Aspire dashboard." The Evaluation Worker and production container path are underdocumented. Recommendation: Architecture.md should list all four services. README "Fast start" section can stay simple (SQLite path), but a callout noting the optional container path would reduce confusion for production users.

### Finding 5: Screenshot narrative drift

`admin-dashboard.png` is labeled "Admin — Routing Dashboard" but `admin-live-routing.png` already exists. Two screenshots showing the same admin UI area create confusion about what "the dashboard" is. Consolidating to the Live Routing shot (more dynamic) as the single admin screenshot eliminates this ambiguity.

---

## 4. Redundancy Across Docs

| Redundancy | Location | Recommendation |
|---|---|---|
| BYOK setup steps appear in both `README.md` and `User_Manual.md` | README §3, User_Manual.md setup section | README keeps the condensed fast-path (it's a quick-start). User_Manual.md is the canonical reference. Add a "→ see User Manual for full details" link at the end of README §3. |
| Model Registry described in both `Architecture.md` and `Model_Registry.md` | Architecture.md §Model Registry & Rules Engine, Model_Registry.md | Architecture.md summary is a one-paragraph pointer — this is correct. No action needed as long as Architecture.md content stays at summary level and doesn't diverge from Model_Registry.md. Currently fine. |

---

## 5. Summary: Top 3 Actionable Findings

1. **Screenshot bloat (README)** — 7 screenshots dilute the README's impact. Cut to aspire-dashboard.png (hero) + admin-live-routing.png (value prop). Everything else lives in contextual doc pages. *Owner: Switch.*

2. **The 5-layer mental model is missing from README** — It's the single clearest explanation of the project and is the anchor for all promo content. It belongs near the top of README. *Owner: Switch/Bruno.*

3. **Test badge is stale** — README badge says 58 tests; decisions.md records 152+. This is a credibility issue if a new visitor clicks the badge. *Owner: Switch (update badge) / Neo (confirm current count).*

---

*This review is for narrative/accuracy input only. Seraph does not edit README.md directly.*
