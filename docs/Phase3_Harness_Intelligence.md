# Phase 3 Harness Intelligence

Phase 3 introduces a safe intelligence layer over existing Phase 1/2 routing contracts, now executed through Microsoft Agent Framework workflows.

## Scope implemented

- Microsoft Agent Framework workflow integration using `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows`.
- Classification Agent (`IClassificationAgent`) with deterministic baseline implementation.
- Rule Advisor Agent (`IRuleAdvisorAgent`) with deterministic baseline implementation.
- Context Providers (`IRequestContextProvider`):
  - requested model
  - streaming flag
  - prompt shape (system message + prompt size)
- Execution traces (`IExecutionTraceStore`) persisted in SQLite via `RoutingExecutionTraceEntity`.

## Key behavior guarantees

- Final route selection still uses `BasicModelRouter.SelectModel(...)`.
- Existing routing/profile contracts are preserved.
- No secrets are stored in traces.
- New trace header is additive only:
  - `x-harness-trace-id`

## Admin inspection

- Retrieve trace details for a routed request:
  - `GET /admin/traces/{traceId}`

Response includes:

- workflow engine label (`microsoft-agent-framework-workflow`)
- classification summary
- rule advisor summary
- final routing decision
- context facts
- workflow steps
