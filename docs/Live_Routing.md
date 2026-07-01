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
| Why | A pipeline sentence, e.g. *"processor 'ollama llama3.1' classified intent=simple-chat (0.92) → rule 'Simple chat' matched → routed to 'ollama llama3.1'."* |
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
| `matchedRuleName` | The rule that matched. For semantic routing this is the rule the processor model picked (from the `semantic.matchedRule` trace fact) |
| `semanticReason` | The processor model's plain-language reason for choosing the rule (semantic routing only) |
| `rawUserMessage` | The original GitHub Copilot payload before `<userRequest>` extraction, shown in a collapsible "Raw Copilot payload" panel for inspection |
| `upstreamStatusCode` | HTTP status the upstream model returned (e.g. `200`, `400`, `500`). `null` if the call never reached the upstream |
| `upstreamLatencyMs` | Round-trip time to the upstream model, in milliseconds (rendered as `1.3s` / `850ms` in the badge) |
| `upstreamSucceeded` | `true` when the upstream call succeeded; `false` drives the red badge and the **Errors only** filter |
| `upstreamError` | The upstream error body/message when the call failed, shown in a collapsible **Upstream error** panel |
| `requestHadTools` | `true` when the incoming request asked the model to call tools (agentic request) — drives the 🛠 **tools** chip |
| `toolCapabilityOverrideApplied` | `true` when the router re-routed the request to a tool-capable model because the originally-selected model can't do tool-calling |
| `overrideReason` | Plain-language reason for the tool-capability override, rendered in the highlighted override note |
| `tokensIn` | GenAI **input** (prompt) tokens reported by the upstream model, when available |
| `tokensOut` | GenAI **output** (completion) tokens reported by the upstream model, when available |
| `tokensTotal` | Total tokens (`tokensIn + tokensOut`) |
| `responseModel` | The model name the upstream response reported (`model` field), which may differ from the requested deployment |

### Upstream outcome, tools, and override (per card)

Each pipeline card now shows an **upstream-outcome** row beneath the "why this rule" line:

- **Success/fail badge.** A green `✅ 200 · 1.3s` badge when `upstreamSucceeded` is `true`,
  or a red `❌ <status or "upstream error"> · <latency>` badge when the upstream call failed.
  Latency renders as seconds (`1.3s`) for calls ≥ 1s and milliseconds (`850ms`) otherwise.
- **Tools chip.** A 🛠 **tools** chip appears when `requestHadTools` is `true`, so you can
  immediately tell an agentic/tool-calling request apart from a plain chat turn.
- **Tokens chip.** A 🔢 `<in> in · <out> out · <total> total` chip appears when the upstream
  reported token usage, so you can see how many tokens each turn consumed at a glance.
- **Upstream error panel.** When `upstreamError` is present, a collapsible **Upstream error**
  panel shows the raw provider message.
- **Override note.** When `toolCapabilityOverrideApplied` is `true`, a prominent warning-style
  callout renders `overrideReason`, e.g. *"Re-routed to 'foundry gpt-5-mini' because this
  request needs tool-calling and the local model can't do it."* This makes it obvious why the
  selected model differs from what the rule would normally pick. See
  [Troubleshooting → Agentic / tool-calling request](Troubleshooting.md#tool-calling).

### "Errors only" filter

Alongside the existing text, model, and rule filters there is an **Errors only** checkbox.
When ticked, the feed shows only requests where `upstreamSucceeded == false`, so you can
zero in on failures without scrolling. It composes with the other filters, resets paging,
survives the 2-second auto-refresh, and is cleared by **Clear filters**.

### Collapsible message detail (semantic routing)

When semantic rules are active, each card adds two collapsible panels:

- **User message** — the **complete** extracted user request (not truncated), so you can read
  exactly what the processor model analyzed.
- **Raw Copilot payload** — the full `<attachments>…<userRequest>…</userRequest>` envelope
  Copilot actually sent, shown only when present. This makes it obvious why a one-word `hi`
  is correctly classified as `hi` rather than as ~1,000 characters of boilerplate.

The "Why this rule" line shows the processor model's `semanticReason` when available
(e.g. *"the user asks to commit and push"*), falling back to the generated explanation
otherwise.

> **Security — API keys are redacted in traces.** Routing traces persist the routing
> *decision*, which includes the resolved model profile. The stored copy of that profile has
> its `apiKey` blanked to `***redacted***` so cloud credentials are never written to the
> trace database (`RoutingExecutionTraces`). The live request to the upstream model still uses
> the real key in memory.


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

## GenAI OpenTelemetry (Aspire dashboard)

Every upstream chat-completion call emits a **GenAI OpenTelemetry client span** so the full
request flow — inbound `POST /v1/chat/completions` → routing → upstream model call — shows up
as a connected trace in the **Aspire dashboard** (and any OTLP backend). The span follows the
[OpenTelemetry GenAI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/):

| Attribute | Example | Meaning |
| --- | --- | --- |
| span name | `chat gpt-5-mini` | `chat {request.model}` |
| `gen_ai.operation.name` | `chat` | The GenAI operation |
| `gen_ai.system` | `ollama` / `azure.ai.openai` | The upstream provider |
| `gen_ai.request.model` | `llama3.1:8b` | Deployment/model sent upstream |
| `gen_ai.response.model` | `gpt-5-mini` | Model the response reported |
| `gen_ai.usage.input_tokens` | `11` | Prompt tokens |
| `gen_ai.usage.output_tokens` | `7` | Completion tokens |
| `harness.routing.profile` / `harness.trace_id` / `harness.stream` / `harness.had_tools` / `harness.tool_override` | — | Harness routing context, to correlate the span with the Live Routing trace |

A **`gen_ai.client.token.usage`** histogram metric is also recorded (dimensioned by
`gen_ai.token.type` = `input`/`output` and `gen_ai.response.model`), so token consumption is
visible on the Aspire dashboard **Metrics** tab. The source and meter are both named
`ElBruno.CopilotHarness.GenAI` and are registered on the tracer/meter providers in
`Program.cs`; export happens automatically when Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT`.

### Token capture

Token usage is parsed from the upstream response body (best-effort — it never alters the
bytes forwarded to the client and never breaks the proxy):

- **Non-streaming** responses are buffered and the top-level `usage` object is read.
- **Streaming** responses are teed: bytes flow to the client immediately while a bounded tail
  is scanned for the final SSE `usage` chunk. To make the upstream emit that chunk, the router
  injects `stream_options.include_usage=true` on streaming requests. This is a spec-compliant
  extra final chunk (empty `choices` + `usage`) that standard OpenAI stream clients — including
  VS Code Copilot — ignore.

Captured tokens are written to the trace as `gen_ai.usage.input_tokens` /
`output_tokens` / `total_tokens` / `gen_ai.response.model` facts, which the Live Routing feed
surfaces as `tokensIn` / `tokensOut` / `tokensTotal` / `responseModel` and the page renders as
the 🔢 tokens chip.

Capture is controlled by `TelemetryOptions`:

| Setting | Default | Description |
| --- | --- | --- |
| `Telemetry:CaptureTokenUsage` | `true` | Capture token usage and inject `stream_options.include_usage` on streaming requests. Set `false` if a client misbehaves with the final usage chunk. |

## Future enhancements

- Server-sent events (`/admin/telemetry/stream`) for push updates instead of polling.
- Per-rule hit counts and cost columns.
- Filtering/search by model, rule, or client, and trace export.
- A dedicated activity-bar tree view in the VS Code extension.
