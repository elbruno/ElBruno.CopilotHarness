# Squad Team

> ElBruno.CopilotHarness - Intelligent BYOK Harness for GitHub Copilot

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Morpheus | Lead Architect | `.squad/agents/morpheus/charter.md` | ✅ Active |
| Neo | Backend/API Engineer | `.squad/agents/neo/charter.md` | ✅ Active |
| Trinity | Frontend Engineer | `.squad/agents/trinity/charter.md` | ✅ Active |
| Niobe | AI/Model Intelligence Engineer | `.squad/agents/niobe/charter.md` | ✅ Active |
| Link | Test & Quality Engineer | `.squad/agents/link/charter.md` | ✅ Active |
| Oracle | GitHub Copilot Integrations Engineer | `.squad/agents/oracle/charter.md` | ✅ Active |
| Dozer | Aspire Platform Engineer | `.squad/agents/dozer/charter.md` | ✅ Active |
| Switch | Developer Experience Engineer | `.squad/agents/switch/charter.md` | ✅ Active |
| Seraph | DevRel & Storytelling Engineer | `.squad/agents/seraph/charter.md` | ✅ Active |
| @copilot | Coding Agent | — | 🤖 Coding Agent |
| Scribe | Session Logger | `.squad/agents/scribe/charter.md` | 📋 Silent |
| Ralph | Work Monitor | `.squad/agents/ralph/charter.md` | 🔄 Monitor |
| Rai | RAI Reviewer | `.squad/agents/Rai/charter.md` | 🛡️ RAI |

<!-- copilot-auto-assign: true -->
### @copilot — Capability Profile

| Capability | Level | Notes |
|-----------|-------|-------|
| Bug fixes (well-scoped) | 🟢 | Best for isolated, test-covered fixes |
| Feature implementation | 🟡 | Works well with clear specs; may need review |
| Refactoring | 🟡 | Handles mechanical refactors; verify scope |
| Architecture decisions | 🔴 | Cannot make cross-cutting design choices |
| Multi-repo coordination | 🔴 | Limited to single-repo context |
| Test writing | 🟢 | Strong at adding tests for existing code |
| Documentation | 🟢 | Generates docs from code effectively |

## Project Context

- **Owner:** Bruno Capuano
- **Project:** ElBruno.CopilotHarness
- **Stack:** .NET 10, ASP.NET Core, Blazor, .NET Aspire, Microsoft Agent Framework, Microsoft.Extensions.AI, EF Core, SQLite
- **Description:** Local-first enterprise-ready Copilot BYOK harness with policy-based model routing, explainability, and continuous evaluation.
- **Created:** 2026-06-26
