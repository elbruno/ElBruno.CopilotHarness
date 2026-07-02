# OllamaProxy — Local Ollama BYOK Proxy for VS Code Copilot

A **minimal, heavily-commented** ASP.NET Core Minimal API that proxies
GitHub Copilot Chat requests to a local [Ollama](https://ollama.com) instance
and exposes an OpenAI-compatible HTTP surface so VS Code can register it as a
**BYOK (Bring Your Own Model)** provider.

> **Why a web app and not a console app?**  
> BYOK in VS Code requires an HTTP endpoint — the editor's model-provider
> configuration points to a URL (`http://localhost:5099/v1/chat/completions`).
> A plain console app has no TCP listener, so VS Code can't reach it.
> ASP.NET Core Minimal API gives us that listener with almost zero ceremony.
> The whole startup is ~10 lines of code.

> **Standalone sample** — this project is intentionally **not** part of the
> main `ElBruno.CopilotHarness.slnx` solution.  It can't break the production
> build and can be understood in isolation.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| [Ollama](https://ollama.com/download) | Running locally on the default port 11434 |
| Ollama model | `ollama pull llama3.1:8b` |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | `dotnet --version` should show 10.x |

---

## Run the proxy

```bash
# from the repo root
cd samples\OllamaProxy
dotnet run
```

The proxy starts on **http://localhost:5099**.

Confirm it is running:

```bash
curl http://localhost:5099/health
# → {"status":"ok","proxy":"OllamaProxy","model":"llama3.1:8b","ollamaUrl":"http://localhost:11434"}
```

Check the models list (used by VS Code BYOK to verify the model ID):

```bash
curl http://localhost:5099/v1/models
```

---

## Quick API test with curl

### Non-streaming request

```bash
curl -s http://localhost:5099/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d "{\"model\":\"llama3.1:8b\",\"stream\":false,\"messages\":[{\"role\":\"user\",\"content\":\"Say hello in one sentence.\"}]}"
```

### Streaming request (SSE)

```bash
curl -N http://localhost:5099/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d "{\"model\":\"llama3.1:8b\",\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\"Count to 5.\"}]}"
```

You should see `data: {...}` lines appearing token-by-token, ending with
`data: [DONE]`.

---

## Register in VS Code as a BYOK model provider

VS Code Copilot supports custom model providers via the
`chatLanguageModels.json` user settings file.  Paste this entry into your
Copilot custom model providers list:

```json
[
  {
    "name": "Ollama BYOK Proxy (sample)",
    "vendor": "customendpoint",
    "apiKey": "ollama-local",
    "apiType": "chat-completions",
    "models": [
      {
        "id": "llama3.1:8b",
        "name": "llama3.1:8b",
        "url": "http://localhost:5099/v1/chat/completions",
        "toolCalling": true,
        "vision": false,
        "maxInputTokens": 128000,
        "maxOutputTokens": 16000
      },
      {
        "id": "copilot-utility-small",
        "name": "Ollama utility (llama3.1:8b)",
        "url": "http://localhost:5099/v1/chat/completions",
        "toolCalling": false,
        "vision": false,
        "maxInputTokens": 128000,
        "maxOutputTokens": 2048
      }
    ]
  }
]
```

> **Why two model entries?**  
> VS Code's agent surface uses a second "utility" model for background tasks
> (chat titles, commit-message suggestions, rename hints).  When the main
> model is BYOK, VS Code cannot use its built-in utility models and needs a
> registered BYOK model to fill that slot.  See the Troubleshooting section
> below for the full explanation.

> **API key note:** Ollama doesn't use API keys for local access.  VS Code
> may still prompt you to enter one.  Any non-empty string (e.g. `ollama-local`)
> works — the proxy ignores it and Ollama doesn't check it.

After registering, open VS Code Copilot Chat, click the model picker, and
select **llama3.1:8b** from the custom providers list.

**For agent mode (the "Describe what to build" surface):** also add the
following two lines to your VS Code `settings.json` so the utility slot is
satisfied (see Troubleshooting for why):

```json
"chat.utilityModel":      "copilot-utility-small",
"chat.utilitySmallModel": "copilot-utility-small"
```

### Serving multiple Ollama models from one proxy

You are **not** limited to a single model.  The proxy discovers every model
Ollama has installed (via Ollama's `GET /api/tags`) at startup and **passes any
of them through untouched** when VS Code requests it.  So you can register as
many models as you like under one provider — they all point at the same
`http://localhost:5099/v1/chat/completions` URL, and each one runs on its
matching Ollama model:

```json
{
  "name": "ollamaRouter",
  "vendor": "customendpoint",
  "apiKey": "ollama-local",
  "apiType": "chat-completions",
  "models": [
    { "id": "llama3.1:8b",         "name": "llama3.1:8b",         "url": "http://localhost:5099/v1/chat/completions", "toolCalling": true, "maxInputTokens": 128000, "maxOutputTokens": 16000 },
    { "id": "qwen2.5:7b-instruct", "name": "qwen2.5:7b-instruct", "url": "http://localhost:5099/v1/chat/completions", "toolCalling": true, "maxInputTokens": 128000, "maxOutputTokens": 16000 }
  ]
}
```

> **The rule:** if the requested `id` is a model Ollama has installed, the proxy
> forwards it **unchanged** (you'll see `[model passthrough]` in the terminal).
> If it isn't — the `copilot-utility-small` alias or a typo — the proxy remaps
> it to the fallback model (`[model rewrite]`).  The model id **must exactly
> match** the Ollama model name (run `ollama list` to see them).  Pulled a new
> model after starting the proxy?  Restart it so it re-discovers the list.

---

## Troubleshooting

### "No utility model is configured for 'copilot-utility-small'"

**What you saw (in the agent surface — the "Describe what to build" input):**

```
No utility model is configured for 'copilot-utility-small'
while the selected main model is BYOK.
```

The reply was tagged `llama3.1:8b`, so the proxy round-trip works.  The error
is about something else entirely.

#### Why this happens

VS Code Copilot's **agent surface** (CHAT / CODEX tabs) keeps **two model slots
running at once**:

| Slot | Purpose | ID it looks for |
|---|---|---|
| **Main model** | Your chat turns (what you type) | the model you picked (e.g. `llama3.1:8b`) |
| **Utility model** | Background micro-tasks: chat title, commit message, rename hints | `copilot-utility-small` (internal ID) |

When you use a built-in GitHub Copilot model, both slots are served by
GitHub's infrastructure automatically.  
When the main model is **BYOK** (a custom endpoint like this proxy), VS Code
can no longer use its hosted utility models — you are effectively offline.
It then looks for a **registered BYOK model** to fill the utility slot.  If
none is found it surfaces the error above.

> **Source:** VS Code BYOK blog post, June 2026:
> *"Set `chat.utilityModel` and `chat.utilitySmallModel` to one of your BYOK
> models to keep those features working."*  
> — https://code.visualstudio.com/blogs/2026/06/18/byok-vscode

---

#### Fix A — Use Ask / Chat mode (quickest for single-model demos)

Switch from the agent "build" surface to the regular **Ask** or **Chat** mode
(the `@workspace` / inline chat / normal chat panel).  Those modes only use the
main model and do not invoke the utility slot.

**Limitation:** you lose multi-step agent workflows.

---

#### Fix B — Point VS Code's utility setting at a registered BYOK model

Add these two lines to your VS Code `settings.json`
(`Ctrl+Shift+P` → **Preferences: Open User Settings (JSON)**):

```json
"chat.utilityModel":      "copilot-utility-small",
"chat.utilitySmallModel": "copilot-utility-small"
```

`copilot-utility-small` must be a model ID that is already registered in your
`chatLanguageModels.json`.  The updated snippet in the **Register in VS Code**
section above includes this entry — add it if you haven't already.

**Why this works:** VS Code forwards utility requests to your proxy with
`model: "copilot-utility-small"`.  The proxy sees the unknown ID and — because
of Fix C below — silently rewrites it to `llama3.1:8b` before Ollama ever sees
it.

---

#### Fix C — This proxy now serves the utility slot too *(default, no config needed)*

Starting with this version the proxy automatically handles the two-model split:

1. **`GET /v1/models`** lists *every installed Ollama model* (discovered at
   startup via Ollama's `/api/tags`) **and** the `copilot-utility-small` alias,
   so VS Code accepts each real model and the utility alias as valid BYOK
   candidates.

2. **`POST /v1/chat/completions`** inspects the `"model"` field on every
   incoming request and either passes it through or remaps it:

   ```
   Installed model  → forwarded UNCHANGED  ([model passthrough])
     Copilot sends:  { "model": "qwen2.5:7b-instruct", ... }  → Ollama (as-is)

   Unknown / alias  → remapped to fallback ([model rewrite])
     Copilot sends:  { "model": "copilot-utility-small", ... }
     Proxy rewrites: { "model": "llama3.1:8b",           ... }  → Ollama
   ```

   You will see a `[model passthrough]` or `[model rewrite]` log line in the
   terminal for each call so it is obvious what is happening on stage.

3. Both the utility alias and the fallback model are configurable via
   `appsettings.json` (`Ollama:UtilityModelId` and `Ollama:DefaultModel`).
   Leave `DefaultModel` unset to auto-pick the first installed model.

**To use Fix C you still need Fix B** (the VS Code settings) — VS Code does
not automatically discover utility models from `GET /v1/models`.  The two fixes
work together: C makes the proxy handle any alias; B tells VS Code *which* alias
to use.

---

**Demo-day checklist for full offline agent surface:**

- [ ] `ollama pull llama3.1:8b` (model is downloaded)  
- [ ] `dotnet run` in `samples\OllamaProxy` (proxy running on port 5099)  
- [ ] `chatLanguageModels.json` has **both** model entries (main + utility)  
- [ ] VS Code `settings.json` has `chat.utilitySmallModel: "copilot-utility-small"`  
- [ ] Model picker → select **llama3.1:8b** → open agent surface → type "hi"  
- [ ] Watch terminal: `[copilot ask]` for user turns + `[model rewrite]` for utility calls

---

Use this as a live presentation progression — each step is a small win that
motivates building a real harness.

### Step 1 — Pure local proxy ✅ *(what this sample does)*

Start the proxy and ask Copilot a question in VS Code.  Copilot answers using
your **local** Ollama model:

- ✅ Privacy — no tokens leave your machine  
- ✅ Works offline  
- ✅ Zero change to the VS Code client — just a model provider URL  

### Step 2 — Observe the ask ✅ *(what this sample does)*

Watch the terminal while you type in Copilot Chat:

```
[copilot ask] explain async/await in C#
```

The proxy logs **only the words you typed**, not the kilobytes of Copilot
boilerplate (`<attachments>`, `<context>`, `<reminderInstructions>`, …).
This is the `CopilotMessageExtractor` class at work — it peels away the
XML envelope VS Code wraps around every message.

This log line is the seed of all intelligence.  Once you can see the ask
clearly, you can act on it.

### Step 3 — Route based on the ask *(next step / what the full harness adds)*

With the extracted ask in hand you could:

```csharp
// Pseudocode — not in this sample
if (typedAsk.Contains("secret") || typedAsk.Contains("password"))
    ForwardToLocalModel(request);   // sensitive — stay local
else
    ForwardToCloudModel(request);   // fine to use cloud
```

The **ElBruno.CopilotHarness** project (`src/ElBruno.CopilotHarness.Router.Api`)
implements this with a full rule engine, priority ordering, regex matching,
and semantic intent classification.

### Step 4 — Add policies, explainability, telemetry *(next steps / full harness)*

Once you control the proxy layer you can add:

- **Cost attribution** — track which team/user is generating which token count  
- **Content policy** — block or transform requests that violate org rules  
- **Explainability** — record why each request was routed to which model  
- **A/B testing** — shadow-send to two models and compare outputs  
- **Audit log** — immutable record of every ask and answer for compliance  

All of these are implemented in the full
[ElBruno.CopilotHarness](../../README.md) project.  This sample distils
just one idea: start with a proxy, observe the ask — everything else follows.

---

## Configuration reference

| Setting | Default | Override |
|---|---|---|
| Ollama base URL | `http://localhost:11434` | `appsettings.json` → `Ollama.BaseUrl` or env `Ollama__BaseUrl` |
| Default / fallback model | `llama3.1:8b` (or first installed) | `appsettings.json` → `Ollama.DefaultModel` or env `Ollama__DefaultModel` |
| Utility model alias | `copilot-utility-small` | `appsettings.json` → `Ollama.UtilityModelId` or env `Ollama__UtilityModelId` |
| Proxy port | **5099** | Change `app.Run(...)` in `Program.cs` |

---

## Files in this sample

| File | Purpose |
|---|---|
| `Program.cs` | Minimal API — proxy + streaming + logging |
| `CopilotMessageExtractor.cs` | Extracts the typed user ask from a Copilot Chat payload |
| `appsettings.json` | Ollama URL + model defaults |
| `OllamaProxy.csproj` | net10.0 ASP.NET Core project — no external NuGet packages |
| `README.md` | This file |
