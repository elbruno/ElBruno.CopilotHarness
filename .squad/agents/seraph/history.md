# Seraph — History

## Seed context (2026-07-01)

- **Project:** ElBruno.CopilotHarness — Local-first, enterprise-ready GitHub Copilot BYOK harness with policy-based model routing, explainability, and continuous evaluation.
- **Stack:** .NET 10, ASP.NET Core, Blazor, .NET Aspire, Microsoft Agent Framework, Microsoft.Extensions.AI, EF Core, SQLite.
- **Owner:** Bruno Capuano (@elbruno).
- **Why I exist:** The team had strong engineering + DevX (Switch) coverage but no one owning promotion, storytelling, pitch, social content, generated imagery, and animated explainers. Hired to make the project presentable and shareable to an audience.
- **The "layers of the harness" mental model** (Bruno's canonical explainer, to animate top-to-bottom):
  1. Copilot (VS Code, Copilot App, CLI, etc.)
  2. BYOK configuration for ElBruno.CopilotHarness
  3. ElBruno.CopilotHarness router running, launched with Aspire
  4. ElBruno.CopilotHarness model selector
  5. Local LLM or Azure OpenAI models
- **Tooling:** `t2i` CLI/skill for text-to-image (Microsoft Foundry: FLUX.2, MAI-Image-2). Prefer self-contained HTML for slides/animations.
- **Partner:** Switch owns README/docs UX mechanics; I own story + promotion. Coordinate on README screenshot reduction.


## Cross-Agent Note (2026-07-01T11-41-11)

**Coherence overlap resolved:** COHERENCE-REVIEW.md flagged README screenshot bloat (recommend 7→2 reduction). Switch simultaneously executed README screenshot reduction (8→1 hero) per user direction. Both concerns addressed: single-hero approach chosen; gallery relocated to docs/screenshots.md. Switch owns the mechanical edit; this assignment flagged the finding. Coordinated successfully.
