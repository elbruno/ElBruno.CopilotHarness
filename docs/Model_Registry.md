# Model Registry

> The Model Registry is the catalog of **LLM connections** the harness can route to.
> Each entry is a concrete, callable model endpoint — not an abstract role.

---

## Concept

Earlier versions of the harness exposed three fixed routing **profiles** (`small`,
`big`, `local`) that all shared a single Microsoft Foundry endpoint and API key. That
model conflated *routing intent* with *connection details* and could only ever talk to
one upstream service.

The Model Registry replaces that with a **flat collection of model connections**. Each
connection is an independent, fully-described endpoint:

| Field | Description |
|---|---|
| `id` | Stable identifier (GUID string). Generated on create. |
| `name` | Unique, human-friendly name (e.g. `ollama llama3.1`, `foundry gpt-5-mini`). **Rules and the default selector reference models by this name.** |
| `type` | Provider type: `ollama` or `azure-openai`. |
| `endpoint` | Base URL of the provider. For Ollama, the server URL (e.g. `http://localhost:11434`). For Azure OpenAI, the resource endpoint. When empty for Azure, the shared Foundry endpoint is used. |
| `modelName` | The upstream model identifier — the Ollama model name **or** the Azure deployment name. |
| `apiVersion` | Azure API version (Azure only; ignored for Ollama). |
| `apiKey` | API key, **encrypted at rest** (Azure only; Ollama needs none). Never returned to the UI in plaintext. |
| `isProcessor` | Marks the **processor model** — the single model used to classify incoming prompts into an intent. At most one connection can be the processor. See [Processor model](#processor-model). |
| `supportsCustomTemperature` | Whether the upstream accepts a non-default `temperature`/`top_p`. When `false`, the harness strips those parameters before forwarding. See [Temperature capability](#temperature-capability). |
| `supportsToolCalling` | Whether the upstream can serve **agentic / tool-calling** requests. When `false`, requests that ask for tools are automatically re-routed to a tool-capable model. Defaults to `true`. See [Tool-calling capability](#tool-calling). |
| `enabled` | Whether the connection is eligible for routing. |

The registry is the single source of truth for "which models exist." Routing rules and
the default-model selector simply point at registry entries by `name`.

---

## Provider types

### Microsoft Foundry Local (`type: foundry-local`)

- Targets the OpenAI-compatible REST endpoint that Foundry Local (or FoundryLocalProxy) exposes
  at `{endpoint}/v1/chat/completions`.
- No API key required.
- `modelName` is the Foundry Local model tag (e.g. `phi-4-mini`).
- Typical `endpoint`: `http://localhost:5101` (FoundryLocalProxy) or `http://localhost:55588`
  (Foundry Local SDK direct).
- Recommended model: **phi-4-mini** — 3.8B parameters, strong JSON output, tool-callable, NPU-capable.
- Install via: `winget install Microsoft.FoundryLocal` then `foundry model run phi-4-mini`.

### Ollama (`type: ollama`)

- Targets the OpenAI-compatible endpoint Ollama exposes at
  `{endpoint}/v1/chat/completions`.
- No API key required.
- `modelName` is the Ollama model tag (e.g. `llama3.1:8b`).
- Typical `endpoint`: `http://localhost:11434`.

### Azure OpenAI / Microsoft Foundry (`type: azure-openai`)

- Targets `{endpoint}/openai/deployments/{modelName}/chat/completions?api-version={apiVersion}`.
- The `endpoint` should be the resource root (e.g. `https://<resource>.openai.azure.com`).
  A trailing `/openai` or `/openai/v1` segment is automatically stripped before the
  deployments path is appended, so those forms work too.
- Requires an `apiKey` (sent as the `api-key` header) unless it falls back to the shared
  Foundry configuration.
- `modelName` is the **deployment name** (e.g. `gpt-5-mini`, `gpt-5.5`).
- `apiVersion` defaults to `2024-10-21`.

---

## Processor model

The harness uses one designated **processor model** to classify each incoming request
before routing it. The processor reads only the first ~200 characters of the prompt and
returns a compact intent label (e.g. `simple-chat`, `github-actions`, `launch-app`,
`code-task`, `long-form`). Routing rules can then match on that intent (see the
`IntentEquals` condition in [Rules Engine](Rules_Engine.md)).

- **Single processor invariant.** At most one connection has `isProcessor = true`. Setting
  it on one model automatically clears it on every other model (enforced server-side).
- **Default.** A fresh database seeds `foundry local phi-4-mini` (`phi-4-mini` via FoundryLocalProxy)
  as the processor. Ollama is seeded alongside but with `isProcessor = false`. Any model type can be
  made the processor — see [Processor Model Setup](./Processor_Model_Setup.md).
- **Real LLM call with deterministic fallback.** The processor is invoked per request via
  its provider. If it is disabled, missing, unreachable, times out, or returns an
  off-vocabulary/unparseable answer, the harness falls back to a fast built-in
  **deterministic** keyword classifier. The path actually used is surfaced on the
  [Live Routing](Live_Routing.md) page as the *classifier source*
  (`processor-model` vs `deterministic`).
- **Tuning.** Configure via the `Classifier` options section: `Enabled` (default `true`),
  `PreviewChars` (default `200`), `TimeoutMs` (default `4000`).

In the Admin UI, the **Models** page shows which connection is the processor and lets you
move the flag with a single toggle.

---

## Temperature capability

Some upstreams (notably `gpt-5-mini`) reject any non-default `temperature` — VS Code
Copilot sends `temperature: 0.1`, which produces a `400 Unsupported value` error. Each
connection therefore carries a `supportsCustomTemperature` flag:

- When `true` (default) the client payload is forwarded unchanged.
- When `false` the harness strips `temperature` and `top_p` from the outgoing payload
  before dispatch, so the upstream applies its own default. This is handled generically by
  `PayloadSanitizer`, so any future model that rejects these parameters just needs the flag
  cleared — no code changes.

The seeded `foundry gpt-5-mini` connection ships with `supportsCustomTemperature = false`.
See [Troubleshooting](Troubleshooting.md#temperature-400) for the end-to-end symptom + fix.

---

## Tool-calling capability {#tool-calling}

Modern Copilot flows are increasingly **agentic**: a request is *streaming* and asks the
model to *call tools* (function/tool calling). To serve these a model must emit well-formed,
*structured* `tool_call` arguments over a stream — many small local models emit empty or
malformed arguments, so the client (VS Code Copilot) reports the request as **failed** even
though the proxy returned `HTTP 200`. Each connection therefore carries a
`supportsToolCalling` flag:

- When `true` (default) the model is considered able to serve tool-calling requests.
- When `false` the model is treated as **chat-only**. If a request that includes
  `tools`/`functions` would otherwise route to this model, the **tool-capability guard**
  overrides the route to a tool-capable model instead.

The guard is also **size-aware**, and applies *even when the routed model is tool-capable*:

- **Small tool requests** (total prompt ≤ `Routing:Rules:LocalToolCallingMaxPromptCharacters`,
  default 12000) prefer a **local (Ollama)** tool-caller, so they stay local.
- **Large agentic payloads** are sent to a **cloud** tool-capable model — even a capable local
  model can't serve a huge working set without over-generating and tripping the client's
  "Response too long" cap.
- Requests routed to a local model are additionally capped at `Routing:Rules:LocalRouteMaxTokens`
  (default 4096) as a runaway safety net.

The override and its reason are surfaced on the [Live Routing](Live_Routing.md) page as a 🛠
**tools** chip plus a highlighted override note. See
[Troubleshooting → "Response too long"](Troubleshooting.md#response-too-long).

### Why `llama3.1:8b` is the local model {#why-llama31}

The single seeded local model — `ollama llama3.1` (`llama3.1:8b`) — is the classifier
(processor), the target of the local routing rules, **and** the local tool-caller. It was
chosen after benchmarking the installed Ollama models against a streaming
`/v1/chat/completions` request that included a `tools` array:

| Model | Streaming `tool_calls` behaviour | Verdict |
|---|---|---|
| **`llama3.1:8b`** | **Structured `tool_calls`, empty content, valid args** (e.g. `{"query":"is:open"}`) | ✅ **Chosen** — clean and reliable |
| `llama3.2:3b` | Structured shape but **empty arguments** (`{"query":""}`) | ❌ Copilot's tool loop fails |
| `qwen2.5:7b-instruct` | Structured `tool_calls` but **leaks preamble** ("Sure,…") and **hallucinates arguments** | ❌ Noisy / wrong arguments |
| `qwen2.5-coder` | **Dumps the call into `content`** instead of `tool_calls` | ❌ Not machine-parseable |
| `gpt-oss:20b` | **No `tool_calls` emitted** over the stream | ❌ Doesn't tool-call |

`llama3.1:8b` was the only installed model that streamed *structured* `tool_calls` with valid
arguments, so it is the local default. A **bigger** local model does *not* fix the
"Response too long" case — that is driven by payload size, not capability, so oversized
agentic payloads still go to the cloud regardless of the local model. Pull it once with
`ollama pull llama3.1:8b`; if it is not installed (or you disable it), tool requests fall back
to the cloud tool-capable model.

See [Troubleshooting → Agentic / tool-calling request](Troubleshooting.md#tool-calling)
for the end-to-end symptom + fix.

In the Admin UI, the **Models** page exposes a *Supports tool-calling* toggle in the model
editor and a 🛠 **tools** capability chip in each model's list row.

---

## API-key encryption

API keys are encrypted at rest using **ASP.NET Core Data Protection**
(`DataProtectionApiKeyProtector`, purpose string
`ElBruno.CopilotHarness.ModelRegistry.ApiKey.v1`):

- **On write** — a non-empty key is encrypted before being persisted to SQLite.
- **On read for routing** — the key is decrypted just-in-time when a request is dispatched.
- **On read for the UI** — the plaintext key is **never** returned. DTOs expose only a
  `hasApiKey` boolean so the UI can show whether a key is configured.
- If the Data Protection key ring is rotated or lost, stored keys can no longer be
  decrypted; `Unprotect` fails closed (returns empty) rather than crashing routing — you
  must re-enter affected keys.

### Upsert semantics for `apiKey`

| `apiKey` value sent | Effect on stored key |
|---|---|
| `null` (omitted) | **Unchanged** — keeps the existing key. |
| `""` (empty string) | **Cleared** — removes the stored key. |
| non-empty | **Replaced** — encrypts and stores the new key. |

---

## CRUD + test flow

All endpoints live under `/admin/models` (see [API Reference](API_Reference.md)):

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/admin/models` | List all connections (no plaintext keys). |
| `GET` | `/admin/models/{id}` | Get one connection. |
| `POST` | `/admin/models` | Create a connection. |
| `PUT` | `/admin/models/{id}` | Update a connection. |
| `DELETE` | `/admin/models/{id}` | Delete a connection. |
| `POST` | `/admin/models/{id}/test` | Connectivity probe — sends a minimal chat completion and reports success + latency. |

In the Admin UI (**Models** page), the form is **type-aware**: choosing *Azure OpenAI*
shows deployment, API version, and API key; choosing *Ollama* shows only the model name.
A **Test connection** button calls the probe endpoint and shows the result inline.

> **Probe payload (per provider).** Azure OpenAI / Foundry models use
> `max_completion_tokens` (the newer gpt-5-series deployments reject the legacy
> `max_tokens` parameter with *"Unsupported parameter… use max_completion_tokens"*),
> while Ollama uses `max_tokens`. When the probe fails, the harness now reads the
> **upstream error body** and surfaces the provider's actual message (e.g. a 400 from
> Azure) inline instead of a generic failure, so you can see exactly why a connection
> was rejected.

---

## Seeded examples

A fresh database is seeded with two example connections so routing works out of the box:

| Name | Type | Endpoint | Model / Deployment | Processor | Custom temp | Tools |
|---|---|---|---|---|---|---|
| `ollama llama3.1` | `ollama` | `http://localhost:11434` | `llama3.1:8b` | ✅ | ✅ | ✅ |
| `foundry gpt-5-mini` | `azure-openai` | *(shared Foundry endpoint)* | `gpt-5-mini` | — | ❌ | ✅ |

`ollama llama3.1` is a single local model that acts as the classifier (processor), the local
rule target, and the local tool-caller — see [Why `llama3.1:8b`](#why-llama31). Pull it once
with `ollama pull llama3.1:8b`.

`foundry gpt-5-mini` is also the seeded **default model**.

> Adapt the seeded entries to your environment: point the Ollama entry at your local
> server and the Foundry entry at your Azure deployment (and add its key).

---

## Related docs

- [Rules Engine](Rules_Engine.md) — how requests are matched to a registry model.
- [Architecture](Architecture.md) — provider abstraction and dispatch pipeline.
- [API Reference](API_Reference.md) — full endpoint contracts.
- [Troubleshooting](Troubleshooting.md) — provider connectivity issues.
