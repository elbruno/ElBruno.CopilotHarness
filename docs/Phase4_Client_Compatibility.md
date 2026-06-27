# Phase 4 Client Compatibility

Phase 4 adds client identity capture while keeping existing OpenAI-compatible endpoints stable.

## Supported client patterns

- **VS Code**: inferred from `User-Agent` values containing `Visual Studio Code`, `vscode`, or `CopilotChat`.
- **Copilot CLI**: inferred from request payload metadata (`metadata.client.name/id/surface`) or compatibility headers.
- **Copilot App**: inferred from `x-copilot-client`/`x-client-name` headers or payload metadata.

All integrations continue to use:

- `POST /v1/chat/completions`
- `POST /v1/responses`
- `GET /v1/models`

## Request metadata capture

Router now captures request-level client metadata and stores it in routing execution traces:

- `request.client.id`
- `request.client.source` (`payload`, `header`, `user-agent`, `unknown`)
- `request.client.version` (when available)
- `request.client.userAgent` (when available)

This makes client identity visible through `GET /admin/traces/{traceId}` for dashboard inspection.

## Response headers (additive)

In addition to existing explainability headers, router responses now include:

- `x-harness-client-id`
- `x-harness-client-source`
- `x-harness-client-version` (when available)

These headers are additive and do not change existing OpenAI-compatible response bodies.

## Dashboard telemetry

Phase 4 dashboard data is exposed from:

- `GET /admin/dashboard/snapshot`

Response includes:

- `connectedClients`: current status for VS Code, Copilot CLI, Copilot App (and unknown clients)
- `liveRequests`: active + very recent routed requests with client id, endpoint, selected profile, trace id
- `generatedAtUtc`: snapshot timestamp

Admin Web home page now refreshes this snapshot continuously to display connected clients and live requests.
