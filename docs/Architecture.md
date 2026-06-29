# Architecture

## Overview

ElBruno.CopilotHarness is a BYOK harness built on .NET 10, ASP.NET Core, .NET Aspire, EF Core, and OpenTelemetry.

## Main components

- **AppHost**: runs the local Aspire application graph and injects external parameters.
- **Router.Api**: OpenAI-compatible request router and telemetry surface.
- **Router.Core**: shared routing contracts, EF Core persistence, and seed data.
- **Admin.Web**: operator UI for setup, models, rules, validation, and dashboards.
- **Judge.Web**: separate AI judge app for prompt replay, manual benchmarks, and evaluation reports.
- **ServiceDefaults**: shared Aspire/Otel/health defaults.
- **Phase 6 backend foundation**: PostgreSQL, Redis, bearer auth for admin endpoints, rate limiting, and queued background jobs.

## Model Registry & Rules Engine

- **Model Registry** — a flat collection of LLM connections (`Models` table). Each entry
  is a concrete endpoint with its own `type` (`ollama` or `azure-openai`), `endpoint`,
  `modelName`/deployment, `apiVersion`, and an optional API key **encrypted at rest** via
  ASP.NET Core Data Protection. Models are referenced by **name**, not by fixed roles.
  See [Model Registry](Model_Registry.md).
- **Provider abstraction** — dispatch goes through `IChatCompletionsProvider`
  implementations selected by provider type by `IChatCompletionsProviderFactory`
  (`AzureFoundryChatCompletionsProvider`, `OllamaChatCompletionsProvider`). Each provider
  uses the resolved connection's own endpoint and key (Azure falls back to shared Foundry
  config when blank), using a named `model-provider` HTTP client.
- **Rules Engine** — ordered, condition-based rules (`RoutingRules` table) evaluated by
  `BasicModelRouter`. An explicit requested model wins first; otherwise enabled rules are
  evaluated by ascending priority and the first match's target model is used; otherwise the
  default model, then the first enabled model. See [Rules Engine](Rules_Engine.md).

## Boundaries

- Router remains the stable client-facing contract.
- Admin features are additive and do not change router semantics.
- Judge features live in a separate application and do not change router semantics.
- Secrets stay in Aspire external parameters.
- Phase 5 features are separate and not mixed into the router runtime surfaces.
- Phase 6 backend concerns stay additive and do not change the OpenAI-compatible router contract.

## Data flow

1. Client request enters `Router.Api`.
2. The Rules Engine selects a registry model (explicit request → rules by priority →
   default → first enabled) and records traces.
3. The provider factory dispatches the request to the selected connection's provider
   (Azure OpenAI/Foundry or Ollama) using that connection's endpoint and decrypted key.
4. Admin pages read persisted model/rule data and telemetry snapshots.
5. Judge app replays/imports prompt records and stores benchmark results.
6. OpenTelemetry exports app/runtime diagnostics.
