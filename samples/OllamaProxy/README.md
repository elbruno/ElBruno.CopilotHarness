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
      }
    ]
  }
]
```

> **API key note:** Ollama doesn't use API keys for local access.  VS Code
> may still prompt you to enter one.  Any non-empty string (e.g. `ollama-local`)
> works — the proxy ignores it and Ollama doesn't check it.

After registering, open VS Code Copilot Chat, click the model picker, and
select **llama3.1:8b** from the custom providers list.

---

## Incremental showcase script

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
| Default model | `llama3.1:8b` | `appsettings.json` → `Ollama.DefaultModel` or env `Ollama__DefaultModel` |
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
