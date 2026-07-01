# Switch — History

## Context

- **Project:** ElBruno.CopilotHarness
- **Owner:** Bruno Capuano
- **Stack:** .NET 10, ASP.NET Core, Blazor, .NET Aspire, EF Core
- **Goal:** Intelligent BYOK harness for GitHub Copilot — policy-based model routing, explainability, continuous evaluation
- **Phases complete:** 0–8 (full PRD delivered)

## Joined

2026-06-27 — hired by Bruno to improve new-user experience, README presentation, and visual documentation.

## Key knowledge

- The app requires Docker/container runtime for full Aspire stack (PostgreSQL + Redis)
- `aspire run` is the official start command (not `dotnet run --project AppHost`)
- Secrets go through Aspire external parameters only — no user-secrets in this project
- `docs/images/` is the home for all screenshots and visual assets
- The README was recently rewritten with a features table, BYOK setup steps, and doc links table — still needs real screenshots


## Cross-Agent Note (2026-07-01T11-41-11)

**Coherence overlap resolved:** Seraph's COHERENCE-REVIEW.md flagged README screenshot bloat (recommend 7→2 reduction). This assignment executed single-hero approach (8→1 hero + gallery relocation to docs/screenshots.md). User chose this direction. Both agents flagged the same concern; coordinated resolution: Switch owns the mechanical README edit; Seraph owns the coherence audit. Completed successfully.
