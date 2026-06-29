# Live Routing Visibility

The **Live Routing** view answers the core question the harness exists to demonstrate:
*"For this incoming prompt, which model was chosen, which rule matched, and why?"*

It is available in two places, both backed by a single shared feed
(`GET /admin/telemetry/feed`):

1. **Admin dashboard** — the **Live Routing** page (`/live`).
2. **VS Code extension** — the **Harness: Show Live Routing** command and a status-bar
   item showing the last routed model.

## What each row shows

The page now presents each request as a **pipeline card** that tells the routing story
left-to-right: *Prompt → Processor classifies intent → Rule matches → Target model → Why*.

| Field | Meaning |
| --- | --- |
| Time | When the request was routed |
| Prompt | Redacted, truncated preview of the **user message** — the actual turn the caller typed, not the system preamble (opt-in — see below) |
| Context | A `📎 {user} of {total} ctx chars · system preamble` badge when the request carried a large system preamble or prior turns, so you can see the user message was tiny even though the full payload was large |
| Processor | Which model classified the request, the **intent** it assigned, and the confidence |
| Classifier source | Whether the intent came from the **processor model** (real LLM call) or the **deterministic** fallback |
| Rule | The routing rule that matched (if any) |
| Model | The selected model + upstream deployment |
| Why | A pipeline sentence, e.g. *"processor 'ollama llama3.2' classified intent=simple-chat (0.92) → rule 'Simple chat' matched → routed to 'ollama llama3.2'."* |
| Client | VS Code Copilot / Copilot CLI / Copilot App (mapped from the user-agent) |
| Trace | Trace id (deep-links to the full routing trace) |

The dashboard also shows per-intent and per-classifier-source summary cards, plus a
per-model share summary (how many requests went to each model).

> **Why "User message" and not "Prompt"?** GitHub Copilot prepends a large boilerplate
> system preamble to every request. The card shows the **last user message** (what you
> actually typed), and routing/classification use that same text — see
> [Rules Engine → User message vs. full payload](Rules_Engine.md#user-message-vs-full-payload).

### Enriched feed fields

`GET /admin/telemetry/feed` surfaces these additional per-request fields used by the cards:

| Field | Meaning |
| --- | --- |
| `classificationIntent` | The intent label (`simple-chat`, `github-actions`, `launch-app`, `code-task`, `long-form`) |
| `classifierSource` | `processor-model` or `deterministic` |
| `processorModel` | Name of the model that performed the classification |
| `classificationConfidence` | Classifier confidence (0–1) |
| `clientDisplayName` | Friendly client name mapped from the user-agent |
| `totalPromptCharacters` | Size of the **full payload** (system preamble + all turns), shown in the context badge |
| `hasSystemMessage` | `true` when the request carried a system message (drives the context badge) |


## Prompt-text capture is opt-in (privacy-first)

By default the router records only a **character count**, never the prompt text. To show
prompt previews, enable capture via `TelemetryOptions`:

| Setting | Default | Description |
| --- | --- | --- |
| `Telemetry:CapturePromptText` | `false` | Capture a prompt preview into the routing trace |
| `Telemetry:PromptPreviewMaxChars` | `2000` | Maximum characters kept in the preview |
| `Telemetry:RedactSecrets` | `true` | Mask emails, bearer tokens, and API keys in the preview |

The local Aspire AppHost sets `Telemetry__CapturePromptText=true` so the view is useful
out of the box during development. For any shared or production deployment, leave it
disabled (or keep redaction on) so raw prompts and secrets are never persisted.

Enable it manually with an environment variable:

```bash
Telemetry__CapturePromptText=true
Telemetry__PromptPreviewMaxChars=2000
Telemetry__RedactSecrets=true
```

## How it works

- Each routed request runs through the routing workflow, which first **classifies** the
  prompt (processor model or deterministic fallback) into an intent, then captures
  **context facts** (stream, prompt characters, requested model, client, intent, classifier
  source, processor model, and — when enabled — a redacted `request.promptPreview`) and a
  **decision** (selected model + reason).
- The decision reason embeds the matched rule name (`Matched rule 'X'.`), which the feed
  promotes to a first-class `matchedRuleName` field and turns into a plain-language
  `explanation`.
- `GET /admin/telemetry/feed` joins these from the persisted execution traces. Both the
  dashboard page and the VS Code panel poll this endpoint (every ~2s on the dashboard).

## Correlation header

Every `/v1/chat/completions` (and `/v1/responses`) response includes
`x-harness-trace-id` (plus `x-harness-model-profile`, `x-harness-model-deployment`,
`x-harness-routing-reason`). A client such as the VS Code extension can use this to
deep-link the exact trace for the chat turn it just sent.

## Future enhancements

- Server-sent events (`/admin/telemetry/stream`) for push updates instead of polling.
- Token/latency/cost columns and per-rule hit counts.
- Filtering/search by model, rule, or client, and trace export.
- A dedicated activity-bar tree view in the VS Code extension.
