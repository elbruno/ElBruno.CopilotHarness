# ElBruno.CopilotHarness

## Vision

An intelligent BYOK Harness for GitHub Copilot built with .NET 10, Aspire and Microsoft Agent Framework.

The project is not just a proxy. It is a platform that provides:

- Intelligent model routing
- Policy based model selection
- Explainability
- Benchmarking
- AI Judge
- Continuous evaluation
- Multi-client support (VS Code, Copilot CLI, Copilot App)

---

# Goals

- Local-first architecture
- Enterprise-ready
- Microsoft-first ecosystem
- OpenAI-compatible API
- Extensible platform

---

# Repository Layout

/
├── README.md
├── LICENSE
├── docs/
├── src/
├── tests/
├── Directory.Build.props
├── Directory.Packages.props
└── global.json

Rules:
- Only README.md and LICENSE live at the repository root.
- All documentation lives under /docs.
- All production code lives under /src.
- All tests live under /tests.

---

# Technology Stack

- .NET 10
- ASP.NET Core
- Blazor
- .NET Aspire
- Aspire Community Toolkit
- Microsoft Agent Framework
- Microsoft.Extensions.AI
- EF Core
- SQLite (Phase 1)
- PostgreSQL + Redis (later)
- OpenTelemetry

---

# Architecture

src/
- ElBruno.CopilotHarness.AppHost
- ElBruno.CopilotHarness.ServiceDefaults
- ElBruno.CopilotHarness.Router.Api
- ElBruno.CopilotHarness.Router.Core
- ElBruno.CopilotHarness.Admin.Web

Future:
- ElBruno.CopilotHarness.Judge.Web
- ElBruno.CopilotHarness.Analytics.Web
- ElBruno.CopilotHarness.VSCode

The Router API must remain small, stable and independent from AI Judge and Analytics.

---

# Storage

Phase 1
- SQLite + EF Core
- Aspire Community Toolkit SQLite resource

Phase 2
- PostgreSQL
- Redis

Secrets
- Managed only through Aspire External Parameters / User Secrets.
- Never stored in SQLite.

---

# Initial Setup

Aspire requests secrets on first execution:
- Foundry Endpoint
- Foundry API Key
- Other provider secrets

The Admin Web Setup Wizard never asks for secrets.

Wizard:
1. Configure Local Model
2. Configure Small Cloud Model
3. Configure Big Cloud Model
4. Choose routing profile
5. Generate my first rules
6. Validate system

---

# Phases

## Phase 0 - BYOK Smoke Test

Goal:
Validate VS Code BYOK against Router.Api.

Features:
- Aspire AppHost
- Router API
- Single hardcoded deployment (gpt-5-mini)
- OpenAI-compatible endpoint
- Health endpoint
- Streaming
- OpenTelemetry

No UI.
No SQLite.
No MAF.

Success:
VS Code -> Router -> GPT-5 Mini.

---

## Phase 1 - Minimal Router

- 3 model support
- Basic rules
- Explainability
- Health checks
- Logging

---

## Phase 2 - Admin

- Blazor Admin
- SQLite
- EF Core
- Setup Wizard
- Generate my first rules
- Model Registry
- Rules Editor
- Playground
- System Validation

---

## Phase 3 - Harness Intelligence

- Microsoft Agent Framework
- Classification Agent
- Routing Workflow
- Rule Advisor Agent
- Context Providers
- Execution traces

---

## Phase 4 - Multi-client

Support:
- VS Code
- Copilot CLI
- Copilot App

Dashboard:
- Connected clients
- Live requests

---

## Phase 5 - AI Judge

Separate application.

Features:
- Replay historical prompts
- Multi-model benchmark
- AI Judge
- Evaluation reports
- Manual benchmark

Judge compares:
- Correctness
- Completeness
- Security
- Best practices
- Cost
- Latency
- Tokens

Never recommends based only on cost.

---

## Phase 6 - Production Backend

- PostgreSQL
- Redis
- Auth
- Rate limiting
- Retry
- Circuit Breakers
- Background jobs

---

## Phase 7 - VS Code Extension

- Status panel
- Explain routing
- Chat participant (@harness)
- Language Model Tools
- Dashboard links

---

## Phase 8 - Continuous Evaluation

- Recommendation Agent
- Shadow routing
- Rule confidence
- Continuous benchmarking
- Human approval
- Team and project profiles

---

# Engineering Guidelines

- Prefer official Microsoft libraries.
- Prefer Aspire integrations over custom infrastructure.
- Use DI everywhere.
- Async only.
- HttpClientFactory only.
- Strongly typed options.
- File scoped namespaces.
- Modern C# features.
- Swagger/OpenAPI.
- Health checks.
- OpenTelemetry.
- Unit tests for every feature.
- Integration tests where applicable.
- Never commit secrets.
- Images generated with ElBruno.Text2Image.Cli.
- Every significant feature must update docs/.

---

# Product Principles

- Measure before recommending.
- Explain every routing decision.
- Human approves rule changes.
- Router stability over feature velocity.
- AI Judge is independent from Router.
- Analytics are independent from Router.
