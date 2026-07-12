# Troubleshooting

## Build or test fails on SQLite warnings

The current dependency graph emits `NU1903` for `SQLitePCLRaw.lib.e_sqlite3`.
This is a known dependency advisory warning in the current package set and does not block the build.

## Foundry secrets missing

If the AppHost asks for Foundry values, start it with `aspire run` and provide `FoundryEndpoint` and `FoundryApiKey` as Aspire external parameters or environment variables.

## Running with PostgreSQL + Redis (optional)

The harness defaults to SQLite — no Docker required. To use PostgreSQL and Redis instead (production-like setup), set `UseContainers=true` in `appsettings.json` of the AppHost or as an environment variable before running.

## Router returns 4xx for malformed input

The router uses OpenAI-style error envelopes for invalid JSON or unsupported request payloads. Check the request body shape and endpoint.

## Admin dashboard shows no activity

- Confirm the AppHost is running.
- Send a routed request through `Router.Api`.
- Refresh the dashboard after a few seconds.

## Model connectivity issues

Use **Test connection** on the Models page (or `POST /admin/models/{id}/test`) to probe a
model. Common failures:

- **Ollama not reachable** — confirm the Ollama server is running and the endpoint is
  correct (default `http://localhost:11434`). The provider calls
  `{endpoint}/v1/chat/completions`; ensure the model is pulled (`ollama pull llama3.1:8b`).
- **Azure key/deployment errors** — verify the endpoint, the **deployment name** (not the
  base model name), the API version, and that the API key is set. A `401`/`403` means the
  key is missing or wrong; a `404` usually means the deployment name is incorrect.
- **Azure `404 Resource not found` with a doubled path** — the Azure endpoint should be the
  resource root, e.g. `https://<resource>.openai.azure.com`. The router appends
  `openai/deployments/{deployment}/chat/completions` itself, so a trailing `/openai` or
  `/openai/v1` segment is automatically stripped to avoid a doubled
  `/openai/v1/openai/deployments/...` path. Any of these forms work:
  `https://<resource>.openai.azure.com`, `.../openai`, or `.../openai/v1`.
- **Azure model falls back to shared Foundry config** — if a model's endpoint or key is
  blank, the Azure provider uses the shared `Foundry:Endpoint` / `Foundry:ApiKey`. Set the
  per-model values to override.
- **Test connection fails with a 400 on a gpt-5-series model** — the newer Azure
  gpt-5-series deployments reject the legacy `max_tokens` parameter
  (*"Unsupported parameter: 'max_tokens'… use 'max_completion_tokens' instead"*). The probe
  now sends `max_completion_tokens` for Azure models (and `max_tokens` for Ollama), so this
  is fixed. When a probe fails, the harness surfaces the **upstream error body** inline, so
  the exact provider message is shown instead of a generic failure.

## Agentic / tool-calling request routed to a local model fails {#tool-calling}

**Symptom.** VS Code Copilot shows the request as **failed** even though the router
returned `HTTP 200`. On the [Live Routing](Live_Routing.md) page the request carries a
🛠 **tools** chip. The upstream-outcome badge may still show `✅ 200`, yet Copilot reports
an error in the editor.

**Cause.** The request was **agentic** — it was *streaming* and asked the model to *call
tools* (function/tool calling). A model can only serve this if it streams well-formed,
*structured* `tool_call` arguments. Many small local models emit empty/malformed arguments,
so Copilot rejects the response on the client side even though the proxy forwarded a `200`.

**Fix.** Each model connection carries a `supportsToolCalling` flag (see
[Model Registry → Tool-calling capability](Model_Registry.md#tool-calling)). The router
detects when an incoming request includes `tools`/`functions` and ensures it is served by a
tool-capable model. The seeded local model **`ollama llama3.1` (`llama3.1:8b`) is
tool-capable** and handles small agentic requests locally; if a request would route to a
model that *isn't* tool-capable, the router **overrides the route** to a tool-capable one
(preferring the local tool-caller for small payloads, the cloud for large ones). Run
`ollama pull llama3.1:8b` once so the local model is available.

`llama3.1:8b` was chosen because it is the only installed model that streams *structured*
`tool_calls` with valid arguments — `llama3.2:3b` (empty args), `qwen2.5:7b-instruct` (leaks
preamble + hallucinated args), `qwen2.5-coder` (dumps to `content`), and `gpt-oss:20b` (no
`tool_calls`) all fail. See [Why `llama3.1:8b`](Model_Registry.md#why-llama31).

On the [Live Routing](Live_Routing.md) page you can now see all of this:

- a 🛠 **tools** chip marks requests that asked for tool-calling,
- a green/red **upstream-outcome** badge shows the real upstream status, latency, and any
  error body,
- a highlighted **override note** explains when a request was re-routed because the original
  model can't do tool-calling, and
- the **Errors only** filter narrows the feed to failed upstream calls.

## "Response too long" on an agentic request {#response-too-long}

**Symptom.** VS Code Copilot fails with **`Reason: Response too long.`** even though the
router returned `HTTP 200`. On the [Live Routing](Live_Routing.md) page the request carries a
🛠 **tools** chip, was overridden to a **local** model, and shows a high latency (e.g. 27 s).

**Cause.** The request was a heavy agentic payload (VS Code sends the *whole* working set —
often 100K+ characters — even for a one-line ask like *"show only open issues"*). A small
local model can't ingest that much context (Ollama also truncates it via a small default
`num_ctx`), so it **over-generates** a runaway response that exceeds the client's output cap,
which VS Code reports as *"Response too long."*

**Fix.** The tool-capability override is now **size-aware**:

- Tool requests whose **total prompt size** is at or below
  `Routing:Rules:LocalToolCallingMaxPromptCharacters` (default **12000**) are kept on the
  local tool-caller.
- Larger agentic payloads are overridden to a **cloud** tool-capable model instead — the
  Live-view override note shows *"…preferring cloud"* with the measured size.

As a second safety net, requests routed to a **local (Ollama)** model have their output capped
at `Routing:Rules:LocalRouteMaxTokens` (default **4096**; set `0` to disable), so a local model
can never run away. Both knobs live under `Routing:Rules` in `appsettings.json`. If large
agentic requests should still run locally, raise `LocalToolCallingMaxPromptCharacters` (and
expect them to be slow / memory-heavy).

## `ERR_CONNECTION_REFUSED` / BYOK endpoint unreachable {#connection-refused}

**Symptom.** VS Code Copilot fails with a *network* error, not an HTTP error:

```
Reason: Please check your firewall rules and network connection then try again.
Error Code: net::ERR_CONNECTION_REFUSED.
```

There is **no matching trace** on the [Live Routing](Live_Routing.md) page for the failed
request, and the last recorded trace shows `HTTP 200`.

**Cause.** `ERR_CONNECTION_REFUSED` means the client could not even open a TCP socket to the
BYOK base URL — the request never reached the router (hence no trace). The router process
(`Router.Api`, default `http://localhost:5117`) was **not listening** at that moment. The most
common trigger while testing the **Launch App actions** rule is self-inflicted: you route a
*"stop the app"* command **through the harness that is serving BYOK**, and the VS Code agent
then executes that stop against the same Aspire app tree that hosts `Router.Api`. Stopping "the
app" stops the endpoint VS Code is talking to, so the *next* request has nothing to connect to.
Confirm with the trace history: the last successful traces will be `launch the app` / `stop the
app` (intent `launch-app`, rule *Launch App actions*, `HTTP 200`), followed by the client-side
connection error with no server trace.

**Fix.**

1. **Restart the harness** so the endpoint is listening again: `aspire start` (wait for
   `Router.Api` to report healthy, then retry in VS Code).
2. **Don't let launch/stop-app commands target the harness itself.** When validating the
   *Launch App actions* rule, point the command at a **separate** target application, or run the
   BYOK harness as an independent, always-on process that your agentic commands never stop.
   Treat the harness as infrastructure — never the app under test.
3. If you ran `aspire start` several times, orphaned MSBuild reuse nodes (`dotnet … MSBuild.dll
   /nodeReuse:true`) can accumulate; clear them with `dotnet build-server shutdown`.
4. **Workspace guardrail (this repo).** `.github/copilot-instructions.md` now tells Copilot to
   treat the harness as background infrastructure: it must never run `aspire stop` / kill
   `Router.Api` for a *"stop the app"* command, *"the app"* always means the workspace
   application under test (never the harness), and **if no application is running, *"stop the
   app"* is a no-op** (Copilot replies that there is nothing to stop instead of tearing anything
   down). This removes the self-inflicted disconnect when you develop and test the harness from
   its own workspace.

> **Note on local routing for these commands.** Real VS Code agentic requests carry the full
> agent system prompt + tools (~47–49 KB), which always exceeds
> `Routing:Rules:LocalToolCallingMaxPromptCharacters` (default **12000**). So even though
> *Launch App actions* targets the **local** model, the size-aware tool override reroutes it to
> the **cloud** tool-capable model every time (the trace's `routing.toolOverrideReason` records
> this). This is expected — see [Agentic / tool-calling](#tool-calling). The local target only
> takes effect for small, tool-free prompts.

## Temperature 400 {#temperature-400}

**Symptom.** A chat request from VS Code Copilot fails with:

```
400 { "error": { "message": "Unsupported value: 'temperature' does not support 0.1
with this model. Only the default (1) value is supported.", "code": "unsupported_value" } }
```

**Cause.** VS Code Copilot sends `temperature: 0.1`. Some models (notably `gpt-5-mini`)
reject any non-default temperature, and the router forwards the client payload upstream
verbatim, so the upstream 400 surfaces to Copilot.

**Fix.** The target model connection carries a `supportsCustomTemperature` flag. Set it to
`false` on the **Models** page for any model that rejects custom temperature; the harness
then strips `temperature` and `top_p` from the outgoing payload (`PayloadSanitizer`) so the
upstream applies its own default. The seeded `foundry gpt-5-mini` ships with this flag
already cleared. See [Model Registry → Temperature capability](Model_Registry.md#temperature-capability).

## Intent classification fell back to deterministic

On the **Live Routing** page the *classifier source* shows `deterministic` instead of
`processor-model`. This means the processor model (default `foundry local phi-4-mini`) could not be
used to classify the prompt, so the built-in keyword classifier was used as a fallback.
Routing still works, but intent quality is lower. Common causes:

- The Foundry Local service (or whichever model is flagged as processor) is not running/reachable.
- No model is flagged as the processor in the registry.
- The processor call timed out (`Classifier:TimeoutMs`, default 4000 ms) or returned an
  unparseable answer.

Start the processor model (see [Processor Model Setup](./Processor_Model_Setup.md)), confirm exactly
one model has the processor flag on the **Models** page, and the source returns to `processor-model`.

## Short prompts (like `hi`) route to the cloud model

On the **Live Routing** page a one-word prompt such as `hi` is classified as `long-form`/
`high` and routed to the cloud model, and the preview shows *"You are an expert AI
programming assistant…"* instead of `hi`.

**Cause.** GitHub Copilot prepends a large boilerplate **system preamble** to every
request. If size/keyword/regex conditions and the classifier look at the whole payload,
every Copilot request looks "large" → the `Large prompts` rule matches → the cloud model is
chosen, and the preview shows the system text.

**Fix.** Routing, classification, and the `/live` preview now use the **last user message**
(the actual turn typed), not the system preamble or prior turns. A short `hi` now classifies
as `simple-chat` and routes to the local model, and the card shows `hi` plus a context badge
(`📎 {user} of {total} ctx chars · system preamble`). See
[Rules Engine → User message vs. full payload](Rules_Engine.md#user-message-vs-full-payload).

## Stored API keys stop working after a reset

API keys are encrypted with ASP.NET Core Data Protection. If the Data Protection key ring
is deleted or rotated (e.g. a clean machine or container without persisted keys), existing
encrypted keys can no longer be decrypted and routing treats them as empty. Re-enter the
affected model API keys on the Models page.

## Database file not found

The default admin database is created under `App_Data\copilotharness-admin.db` after first run. If needed, override `Persistence__DatabasePath`.

## Request times out after ~30s on a reasoning model {#upstream-timeout}

**Symptom.** A request that routes to a cloud reasoning model (e.g. `foundry gpt-5-mini`)
fails after roughly 30 seconds with a `500` and an inner
`Polly.Timeout.TimeoutRejectedException: The operation didn't complete within the allowed
timeout of '00:00:30'`. Simple prompts to the same model succeed; only large/complex prompts
fail.

**Cause.** The shared HTTP resilience handler (`AddStandardResilienceHandler` in
`ServiceDefaults`) applied its default **30-second total / 10-second per-attempt** timeout to
every HTTP client, including the model-provider clients. Reasoning models routinely take far
longer than 30s on heavy prompts, so the call was cancelled before the model responded.

**Fix.** The resilience handler is now tuned for an LLM proxy: a **5-minute attempt timeout**,
a **10-minute total timeout**, and **retries disabled** (retrying an expensive or streaming
chat completion would duplicate the generation and double-bill). Complex prompts now run to
completion. See `ServiceDefaults/Extensions.cs`.

## Chat request returns 500 with "Invalid non-ASCII or control character in header" {#header-500}

**Symptom.** A routed chat request fails with `500` and
`System.InvalidOperationException: Invalid non-ASCII or control character in header: 0x2192`.

**Cause.** The router echoes the routing decision into the `x-harness-routing-reason` response
header. Semantic-rule reasons contain an arrow (`→`, `0x2192`) and the processor model's
free-text explanation can include arbitrary Unicode. HTTP header values must be ASCII, so
Kestrel rejected the response.

**Fix.** Header values are now ASCII-sanitized before they are written
(`OpenAiApiUtilities.SanitizeHeaderValue`): common symbols (`→`, smart quotes, en/em dashes,
ellipsis) are mapped to ASCII and any other non-printable/non-ASCII character is dropped. The
full, unmodified reason is still available in the routing trace and on the
[Live Routing](Live_Routing.md) page.

---

## Foundry Local / phi-4-mini {#foundry-local}

### phi-4-mini not detected — health check shows Unhealthy

**Symptom.** The `/health` endpoint reports `foundry-local-endpoint: Unhealthy`. Live Routing
shows `⚙️ Keyword/heuristic fallback` (deterministic classifier) instead of `🧠 Decided by local model`.

**Cause.** Foundry Local (or FoundryLocalProxy) is not running at the configured endpoint
(`http://localhost:5101` by default).

**Fix — option 1 (Foundry Local CLI):**
```
winget install Microsoft.FoundryLocal
foundry model run phi-4-mini
```

**Fix — option 2 (FoundryLocalProxy):**
```
cd C:\src\ElBruno.LLMProxies\src\proxies\FoundryLocalProxy
dotnet run
```

Verify it is reachable:
```
curl http://localhost:5101/v1/models
```

### Custom endpoint

If you run Foundry Local on a different port, override in `appsettings.json` or as an environment variable:
```json
"FoundryLocal": { "Endpoint": "http://localhost:55588" }
```
Or as an environment variable: `FoundryLocal__Endpoint=http://localhost:55588`.

### Processor model still shows as Ollama after migration

If the Admin UI's **Setup** page still shows an Ollama model as the processor:

1. Open **Model Registry** (`/models`).
2. Find `foundry local phi-4-mini` — enable **Is Processor** and click Save.
3. Find the Ollama model — disable **Is Processor** and click Save.

On a fresh database the seed takes care of this automatically; on an upgraded database you may
need to flip the flags manually (the `foundry local phi-4-mini` entry is inserted automatically
by the upgrade migration, but `IsProcessor` is not automatically toggled to avoid unexpected
behavior changes on existing systems).

### Intent classification still shows `deterministic` after starting Foundry Local

Common causes:

- The `FoundryLocal.Endpoint` in `appsettings.json` does not match the running instance's port.
- No model in the registry has `IsProcessor = true` — check the Models page.
- The classifier timed out (`Classifier:TimeoutMs`, default 4000 ms) — increase if phi-4-mini
  is slow to load the first time.
- phi-4-mini returned an off-schema response — this can happen on first load while the model
  warms up. Retry after a few seconds.
