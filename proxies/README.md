# Local Proxies

Three minimal ASP.NET Core proxies that let VS Code Copilot treat different model
providers as BYOK (Bring Your Own Model) endpoints — no Copilot modification needed.

Each proxy exposes an **OpenAI-compatible `/v1/chat/completions` endpoint**, so any
tool that speaks the OpenAI API works without changes.

---

## Proxies

| Proxy | Port | Backend | Notes |
|---|---|---|---|
| [OllamaProxy](OllamaProxy/README.md) | 5099 | Local Ollama | Forwards to a running Ollama instance. Discovers installed models automatically. |
| [FoundryProxy](FoundryProxy/README.md) | 5100 | Azure OpenAI / Foundry cloud | Requires credentials stored in `.NET User Secrets` — never touches the repo. |
| [FoundryLocalProxy](FoundryLocalProxy/README.md) | 5101 | Microsoft Foundry Local (offline) | Uses the Foundry Local C# SDK. Downloads the model on first run, then fully offline. NPU-capable. |

---

## Running the proxies

### Option A — Single proxy (standalone)

Each proxy is a self-contained ASP.NET Core app. No Aspire required.

```bash
cd proxies/OllamaProxy      # or FoundryProxy, FoundryLocalProxy
dotnet run
```

### Option B — All three + test UI via Aspire

From the `proxies/` folder, a single command starts all three proxies **and** the
Blazor test app, wired together in the Aspire dashboard:

```bash
cd proxies
aspire start
```

> **Requires the Aspire CLI** — install with `dotnet workload install aspire`.  
> `aspire.config.json` in this folder already points at `AppHost/AppHost.csproj`,
> so `aspire start` (and `aspire stop`) work without any extra flags.

What Aspire starts:

| Service | URL | What it is |
|---|---|---|
| `ollama-proxy` | http://localhost:5099 | OllamaProxy |
| `foundry-proxy` | http://localhost:5100 | FoundryProxy |
| `foundry-local-proxy` | http://localhost:5101 | FoundryLocalProxy |
| `proxies-test-app` | http://localhost:5102 | Blazor test UI (see below) |
| Aspire dashboard | printed in console | Logs, health, traces for all services |

To stop everything:

```bash
aspire stop
```

---

## Pre-requisites per proxy

| Proxy | What you need before starting |
|---|---|
| OllamaProxy | [Ollama](https://ollama.com) running on `localhost:11434` with at least one model pulled |
| FoundryProxy | `dotnet user-secrets set` in the `FoundryProxy/` folder (Endpoint, ApiKey, Deployment) |
| FoundryLocalProxy | Nothing — the SDK downloads `phi-4-mini` (~2.5 GB) on first run and caches it. Internet needed only on first run. |

---

## ProxiesTestApp — Blazor test UI

`ProxiesTestApp/` is a Blazor Server web app for testing all three proxies from a
browser. It starts automatically with `aspire start`.

| Page | URL | What it does |
|---|---|---|
| Health Dashboard | `/` | Live status cards for all three proxies, auto-refreshes every 5 s |
| Chat | `/chat` | Streaming or non-streaming chat — pick a proxy, model, and optional system prompt. Shows tok/s and active model. |
| Compare | `/compare` | Same prompt sent to all three proxies at once, token-by-token side-by-side |
| Models | `/models` | **FoundryLocalProxy only** — live model catalog with cached/loaded status, Load/Unload/Delete actions, SSE progress during model loading |
| History | `/history` | Request log — every chat request recorded with proxy, model, latency, token count |
| Setup | `/setup` | VS Code BYOK config generator — correct `chatLanguageModels.json` snippets for each proxy, official doc links |

> **FoundryLocalProxy model management:** if a model is not yet loaded, chat requests return a clear error
> indicating the model is unavailable. Use the **Models** page to download and load the model before chatting.
> Unloading a model frees GPU/RAM immediately; deleting it removes the cached weights from disk entirely.

To run the test app on its own (while the proxies are already running):

```bash
cd proxies/ProxiesTestApp
dotnet run
# → http://localhost:5102
```

---

> **New here?** Start with `OllamaProxy` — simplest possible version, single `dotnet run`.  
> Add `FoundryLocalProxy` for offline / NPU inference.  
> Add `FoundryProxy` for cloud models with proper secret management.  
>
> The full **[ElBruno.CopilotHarness](../README.md)** router builds on all three patterns,
> dynamically routing each Copilot request to the right provider based on policy rules.
