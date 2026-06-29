# Live Routing Visibility

The **Live Routing** view answers the core question the harness exists to demonstrate:
*"For this incoming prompt, which model was chosen, which rule matched, and why?"*

It is available in two places, both backed by a single shared feed
(`GET /admin/telemetry/feed`):

1. **Admin dashboard** — the **Live Routing** page (`/live`).
2. **VS Code extension** — the **Harness: Show Live Routing** command and a status-bar
   item showing the last routed model.

## What each row shows

| Column | Meaning |
| --- | --- |
| Time | When the request was routed |
| Prompt | Redacted, truncated preview of the prompt (opt-in — see below) |
| Model | The selected model profile + upstream deployment |
| Rule | The routing rule that matched (if any) |
| Explanation | A human sentence, e.g. *"Routed to 'small' because rule 'Short prompts' matched. Classified as conversational/low."* |
| Client | VS Code / Copilot CLI / Copilot App |
| Trace | Trace id (deep-links to the full routing trace) |

The dashboard also shows a per-model share summary (how many requests went to each model).

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

- Each routed request runs through the routing workflow, which captures **context facts**
  (stream, prompt characters, requested model, client, and — when enabled — a redacted
  `request.promptPreview`) and a **decision** (selected model + reason).
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
