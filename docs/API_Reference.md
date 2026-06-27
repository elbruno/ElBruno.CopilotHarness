# API Reference

## Router

### `POST /v1/chat/completions`

OpenAI-compatible chat-completions endpoint.

### `GET /v1/models`

Returns logical model profiles and configured deployments.

### `POST /v1/responses`

Minimal compatibility endpoint layered over the current router pipeline.

### `GET /health`

Readiness check including downstream Foundry reachability.

### `GET /alive`

Liveness check.

### `GET /v1/status`

Returns the VS Code extension status surface, including dashboard link metadata and health checks.

### `GET /v1/explain-routing/{traceId}`

Returns routing trace details for a trace id.

### `GET /v1/extension/capabilities`

Returns the chat participant, dashboard link, and language model tool metadata used by the VS Code extension.

## Admin

### `GET /admin/dashboard/snapshot`

Returns connected clients and live requests for the Admin dashboard.

### `GET /admin/operations/status`

Returns the Phase 6 operational readiness snapshot for auth, rate limiting, backoff, background jobs, and infrastructure.

### `GET /admin/traces/{traceId}`

Returns routing trace details for a routed request.

### `GET /admin/clients/connected`

Returns current connected client summary.

### `GET /admin/requests/live`

Returns live/recent routed requests.

## Judge

### `GET /`

Returns the Judge operations dashboard with benchmark and storage status.

### `POST /judge/prompt-records/import`

Imports historical prompt records for evaluation.

### `GET /judge/historical/suites`

Lists built-in historical prompt suites.

### `GET /judge/historical/suites/{suiteId}`

Returns one historical prompt suite.

### `POST /judge/historical/suites/{suiteId}/replay`

Imports a suite and replays it across selected models.

### `POST /judge/benchmarks/replay`

Replays stored prompt records across multiple models.

### `POST /judge/benchmarks/manual`

Runs a manual benchmark for one-off prompts.

### `GET /judge/reports/{runId}`

Returns benchmark results and model summaries.

## Notes

- All compatibility additions are additive and preserve existing response shapes.
- Refer to `docs/Phase4_Client_Compatibility.md` for client detection details.
- Refer to `docs/PRD.md` Phase 7 for VS Code extension integration surfaces.
- Admin routes require bearer auth when `Backend:Auth:AdminApiKey` is configured.
- Requests are subject to configurable rate limiting.
