# Documentation Index

> One-stop navigation for the ElBruno.CopilotHarness project. All links are relative to the `docs/` folder unless otherwise noted.

---

## Start here

| Doc | Description |
|---|---|
| [README](../README.md) | Project overview, fast start, and BYOK setup |
| [ElBruno.LLMProxies](https://github.com/elbruno/ElBruno.LLMProxies) | Standalone proxy stack repository (FoundryProxy, FoundryLocalProxy, OllamaProxy, analytics) |
| [Current Progress](Current_Progress.md) | Phase status at a glance — what's done, what's next |
| [User Manual](User_Manual.md) | Full walkthrough: install, configure, and operate the harness |
| [Architecture](Architecture.md) | System design, component boundaries, and data flow |
| [API Reference](API_Reference.md) | All router and admin HTTP endpoints with request/response shapes |
| [Troubleshooting](Troubleshooting.md) | Common issues and step-by-step fixes |
| [Logging](Logging.md) | Reading router logs, the structured upstream log line, log levels, and filtering for real errors |
| [Runbook](Runbook.md) | Operational playbook: start, stop, reset, inspect, and recover |
| [Contributing](Contributing.md) | Local dev setup, workflow, Docker notes, and good-first-issue ideas |

---

## Core features

| Doc | Description |
|---|---|
| [Model Registry](Model_Registry.md) | Multi-provider LLM connections (Ollama, Azure OpenAI/Foundry), fields, API-key encryption, CRUD + test |
| [Rules Engine](Rules_Engine.md) | Condition-based routing rules, priority/default, first-run wizard, and rule testing |
| [Live Routing](Live_Routing.md) | Visual prompt → model → rule → explanation feed (dashboard + VS Code), opt-in prompt capture |

---

## Phase docs

| Doc | Description |
|---|---|
| [Phase 2 — Admin](Phase2_Admin.md) | Admin API and dashboard: model management and routing history |
| [Phase 3 — Harness Intelligence](Phase3_Harness_Intelligence.md) | Rules engine, AI routing agents, and profile selection logic |
| [Phase 4 — Client Compatibility](Phase4_Client_Compatibility.md) | OpenAI-compatible proxy layer and Copilot BYOK wiring |
| [Phase 4 — Multi-Client Dashboard](Phase4_MultiClient_Dashboard.md) | Real-time multi-client view with SignalR and Blazor |
| [Phase 5 — AI Judge](Phase5_AI_Judge.md) | Prompt replay, model benchmarking, and AI-scored quality evaluation |
| [Phase 7 — VS Code Extension](Phase7_VSCode_Extension.md) | In-editor routing explainer and dashboard launcher |
| [Phase 8 — Continuous Evaluation](Phase8_Continuous_Evaluation.md) | Shadow routing, confidence scoring, and human-approval workflow for rule changes |

---

## Planning

| Doc | Description |
|---|---|
| [PRD](PRD.md) | Product Requirements Document — goals, phases, and success criteria |
| [Copilot Implementation Prompt](Copilot_Implementation_Prompt.md) | Authoritative phase-by-phase implementation workflow for AI assistants |
