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

### `GET /v1/vscode-config`

Returns a ready-to-paste `chatLanguageModels.json` document for GitHub Copilot's BYOK custom endpoint.
The chat completions URL is derived from the inbound request host, so it is correct for whatever
host/port you reached the router on. Optional query parameters: `modelId` (the model id/label, default
`elbruno.copilotharness`) and `name` (the config name, default `SmartRouter`).

### `GET /connect`

A friendly HTML page that renders the `chatLanguageModels.json` config with **Copy** and
**Download** helpers plus step-by-step VS Code instructions. Open `http://localhost:5117/connect`.

## Admin

### Model Registry — `/admin/models`

Multi-provider LLM connections. See [Model Registry](Model_Registry.md).

| Method | Route | Description |
|---|---|---|
| `GET` | `/admin/models` | List all model connections. API keys are never returned; each entry exposes `hasApiKey`. |
| `GET` | `/admin/models/{id}` | Get one model connection. |
| `POST` | `/admin/models` | Create a connection. Body: `ModelConnectionUpsertRequest` `{ name, type, endpoint, modelName, apiVersion, apiKey?, enabled, isProcessor?, supportsCustomTemperature? }`. `type` is `ollama` or `azure-openai`. `isProcessor=true` clears the flag on all other models (single-processor invariant). `supportsCustomTemperature` defaults to `true`. |
| `PUT` | `/admin/models/{id}` | Update a connection. `apiKey`: `null` keeps the existing key, `""` clears it, non-empty replaces it. |
| `DELETE` | `/admin/models/{id}` | Delete a connection. |
| `POST` | `/admin/models/{id}/test` | Connectivity probe. Returns `{ success, message, latencyMs }`. |

`ModelConnectionDto`: `{ id, name, type, endpoint, modelName, apiVersion, hasApiKey, enabled, isProcessor, supportsCustomTemperature, updatedAtUtc }`.

### Rules Engine — `/admin/rules`

Condition-based routing rules + default model. See [Rules Engine](Rules_Engine.md).

| Method | Route | Description |
|---|---|---|
| `GET` | `/admin/rules` | List all rules. |
| `GET` | `/admin/rules/{id}` | Get one rule. |
| `POST` | `/admin/rules` | Create a rule. Body: `RoutingRuleUpsertRequest` `{ name, description, conditionType, conditionValue, targetModel, priority, enabled }`. |
| `PUT` | `/admin/rules/{id}` | Update a rule. |
| `DELETE` | `/admin/rules/{id}` | Delete a rule. |
| `POST` | `/admin/rules/wizard` | Generate the starter rule set (first-run). |
| `POST` | `/admin/rules/test` | Dry-run evaluation. Body: `{ prompt, systemMessage?, stream, requestedModel? }`. Returns `{ matchedRuleName, selectedModel, reason, promptCharacters }`. |
| `GET` | `/admin/rules/default` | Get the default model name. |
| `PUT` | `/admin/rules/default` | Set the default model. Body: `{ modelName }`. |

`conditionType` is one of: `Always`, `PromptSizeAtLeast`, `IsStreaming`, `HasSystemMessage`, `RequestedModelEquals`, `PromptContainsKeyword`, `PromptMatchesRegex`, `IntentEquals`. For `IntentEquals`, `conditionValue` is an intent label (`simple-chat`, `github-actions`, `launch-app`, `code-task`, `long-form`) produced by the processor-model classifier — see [Rules Engine](Rules_Engine.md#intent-classification).

`RoutingRuleDto`: `{ id, name, description, conditionType, conditionValue, targetModel, priority, enabled, updatedAtUtc }`.

### Setup — `/admin/setup`

| Method | Route | Description |
|---|---|---|
| `GET` | `/admin/setup/state` | Returns setup completion state and the default model. |
| `POST` | `/admin/setup/wizard` | Complete first-run setup. Body: `{ defaultModel, generateFirstRules }`. |
| `POST` | `/admin/setup/generate-first-rules` | Generate the starter rule set. |

### `GET /admin/dashboard/snapshot`

Returns connected clients and live requests for the Admin dashboard.

### `GET /admin/telemetry/feed`

Returns the **Live Routing** feed — one row per routed request combining prompt preview, selected model, matched rule, and a human explanation. Query string: `?limit=` (1–200, default 50). Response shape:

```json
{
  "generatedAtUtc": "2026-...",
  "promptCaptureEnabled": true,
  "requests": [
    {
      "traceId": "trace-...",
      "createdAtUtc": "2026-...",
      "clientId": "vscode",
      "clientDisplayName": "VS Code",
      "endpoint": "/v1/chat/completions",
      "stream": true,
      "requestedModel": "elbruno.copilotharness",
      "selectedModel": "small",
      "deployment": "llama3.2",
      "matchedRuleName": "Simple chat",
      "reason": "Matched rule 'Simple chat'.",
      "explanation": "processor 'ollama llama3.2' classified intent=simple-chat (0.92) → rule 'Simple chat' matched → routed to 'ollama llama3.2'.",
      "promptPreview": "hi",
      "promptCharacters": 2,
      "totalPromptCharacters": 2480,
      "hasSystemMessage": true,
      "classificationIntent": "simple-chat",
      "classificationComplexity": "low",
      "classifierSource": "processor-model",
      "processorModel": "ollama llama3.2",
      "classificationConfidence": 0.92
    }
  ]
}
```

`promptCharacters` is the size of the **user message** (the actual ask, used for routing),
while `totalPromptCharacters` is the size of the full payload including Copilot's system
preamble and prior turns. `hasSystemMessage` is `true` when the request carried a system
message — both drive the Live Routing context badge.

`classifierSource` is `processor-model` when the designated processor model classified the
request, or `deterministic` when the built-in fallback was used (processor disabled,
unreachable, timed out, or returned an unusable answer). `processorModel` names the model
that performed the classification. `clientDisplayName` is mapped from the user-agent (e.g.
VS Code Copilot).

`promptPreview` is only populated when `Telemetry:CapturePromptText=true` on the router (truncated to `Telemetry:PromptPreviewMaxChars`, secrets redacted when `Telemetry:RedactSecrets=true`). When capture is off, `promptCaptureEnabled` is `false` and `promptPreview` is empty, but the model/rule/explanation are still returned.

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
