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

## Boundaries

- Router remains the stable client-facing contract.
- Admin features are additive and do not change router semantics.
- Judge features live in a separate application and do not change router semantics.
- Secrets stay in Aspire external parameters or user secrets.
- Phase 5 features are separate and not mixed into the router runtime surfaces.

## Data flow

1. Client request enters `Router.Api`.
2. Routing workflow selects a model/profile and records traces.
3. Admin pages read persisted routing/admin data and telemetry snapshots.
4. Judge app replays/imports prompt records and stores benchmark results.
5. OpenTelemetry exports app/runtime diagnostics.
