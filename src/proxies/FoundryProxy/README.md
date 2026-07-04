# FoundryProxy — Microsoft Foundry BYOK Proxy for VS Code Copilot

A **minimal, heavily-commented** ASP.NET Core Minimal API that proxies
GitHub Copilot Chat requests to an **Microsoft Foundry** (Azure OpenAI) deployment
and exposes an OpenAI-compatible HTTP surface so VS Code can register it as a
**BYOK (Bring Your Own Key / Bring Your Own Model)** provider.

The Azure credentials live in **.NET User Secrets** — they never touch the VS Code
client, the repo, or any config file that could be committed.

> **Why a web app and not a console app?**  
> BYOK in VS Code requires an HTTP endpoint — the editor's model-provider
> configuration points to a URL (`http://localhost:5100/v1/chat/completions`).
> A plain console app has no TCP listener, so VS Code can't reach it.
> ASP.NET Core Minimal API gives us that listener with almost zero ceremony.
> The whole startup is ~10 lines of code.

> **Standalone sample** — this project is intentionally **not** part of the
> main `ElBruno.CopilotHarness.slnx` solution.  It can't break the production
> build and can be understood in isolation.

> **No Azure SDK required** — this proxy speaks raw HTTP to the Azure OpenAI
> REST API using `HttpClient`.  This keeps the sample dependency-free and
> makes the auth header (`api-key`) and URL structure fully visible on stage.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| [Microsoft Foundry](https://ai.azure.com) or [Azure OpenAI](https://portal.azure.com) resource | With at least one deployed model (e.g. `gpt-4o-mini`) |
| Azure OpenAI Endpoint | `https://<resource-name>.openai.azure.com` (from the Azure portal) |
| Azure OpenAI API Key | Primary or Secondary key from the Azure portal |
| Deployment name | The name you gave the deployment (e.g. `gpt-4o-mini`) — **not** the base model name |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | `dotnet --version` should show `10.x` |

---

## User Secrets setup

> **Security teaching point:** User Secrets are the right way to handle
> developer credentials in .NET.  They are stored in a per-user directory
> **outside** the repository — so they can never be accidentally committed.
>
> On **Windows**, secrets are stored in:
> ```
> %APPDATA%\Microsoft\UserSecrets\adc0ec66-b21e-4f58-93a3-14e8049f8b55\secrets.json
> ```
> (`adc0ec66-…` is the `UserSecretsId` from `FoundryProxy.csproj`)
>
> On **macOS / Linux**:
> ```
> ~/.microsoft/usersecrets/adc0ec66-b21e-4f58-93a3-14e8049f8b55/secrets.json
> ```
>
> The `UserSecretsId` is safe to commit — it is just a lookup key.
> **The values inside `secrets.json` are never committed.**

Run these commands once (from the `samples\FoundryProxy` directory):

```powershell
cd samples\FoundryProxy

# The UserSecretsId is already in FoundryProxy.csproj — no need to run `dotnet user-secrets init`.
# Just set the three required secrets:

dotnet user-secrets set "Foundry:Endpoint"   "https://<your-resource>.openai.azure.com"
dotnet user-secrets set "Foundry:ApiKey"     "<your-key>"
dotnet user-secrets set "Foundry:Deployment" "gpt-4o-mini"
```

Verify they were stored:

```powershell
dotnet user-secrets list
```

Expected output:

```
Foundry:Endpoint   = https://<your-resource>.openai.azure.com
Foundry:ApiKey     = <your-key>
Foundry:Deployment = gpt-4o-mini
```

### What goes where

| Setting | Where to set it | Notes |
|---|---|---|
| `Foundry:Endpoint` | User Secrets | Azure portal → Azure OpenAI → Keys and Endpoint |
| `Foundry:ApiKey` | User Secrets | Azure portal → Azure OpenAI → Keys and Endpoint |
| `Foundry:Deployment` | User Secrets | Azure portal → Azure OpenAI → Deployments → Name |
| `Foundry:ApiVersion` | `appsettings.json` | Default `2024-10-21` — safe to commit |
| `Foundry:UtilityModelId` | `appsettings.json` | Default `copilot-utility-small` — safe to commit |

---

## Run the proxy

```powershell
cd samples\FoundryProxy
dotnet run
```

The proxy starts on **http://localhost:5100**.

If secrets are missing you will see a yellow banner with the exact `dotnet user-secrets set` commands.  The server still starts — `/health` responds immediately so you can diagnose the state.

Confirm it is running and configured:

```bash
curl http://localhost:5100/health
```

**Configured response:**
```json
{"status":"ok","proxy":"FoundryProxy","configured":true,"deployment":"gpt-4o-mini","targetHost":"<resource>.openai.azure.com","apiVersion":"2024-10-21","utilityModel":"copilot-utility-small"}
```

**Unconfigured response:**
```json
{"status":"not configured","proxy":"FoundryProxy","configured":false,...,"hint":"Run: dotnet user-secrets set ..."}
```

Check the models list (used by VS Code BYOK to verify the model ID):

```bash
curl http://localhost:5100/v1/models
```

---

## Quick API test with curl

### Non-streaming request

```bash
curl -s http://localhost:5100/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d "{\"model\":\"gpt-4o-mini\",\"stream\":false,\"messages\":[{\"role\":\"user\",\"content\":\"Say hello in one sentence.\"}]}"
```

### Streaming request (SSE)

```bash
curl -N http://localhost:5100/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d "{\"model\":\"gpt-4o-mini\",\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\"Count to 5.\"}]}"
```

You should see `data: {...}` lines appearing token-by-token, ending with
`data: [DONE]`.

---

## How the proxy processes a request

Every `POST /v1/chat/completions` call flows through three steps in
`Program.cs`.  Understanding them is the whole point of the sample — this is
where a "dumb pipe" becomes a *harness*.

```
VS Code Copilot ──▶ ① Buffer body ──▶ ② Parse once ──▶ ③ Forward to Azure ──▶ relay response
                                          │
                                          ├─ observe the ask     (CopilotMessageExtractor)
                                          ├─ detect streaming    ("stream": true?)
                                          ├─ resolve deployment  (model → Azure deployment)
                                          └─ strip sampling params (temperature/top_p ≠ 1)
```

| Step | What happens | Why |
|---|---|---|
| **① Buffer** | Read the raw request body into a string once. | The body is a forward-only stream — you can only read it once, so we buffer it before both parsing **and** forwarding. |
| **② Parse once** | Parse the JSON a single time into a `JsonObject` and reuse it for all tasks below. | Avoids deserializing the same payload repeatedly. |
| **③ Forward** | POST the (possibly modified) body to Azure OpenAI at `openai/deployments/{deployment}/chat/completions?api-version=…`, authenticated with the server-side `api-key` header. | Azure selects the model by the **deployment in the URL path**, and authenticates with `api-key` (not a bearer token). |

### ② a — Parsing the Copilot message (`CopilotMessageExtractor`)

VS Code Copilot Chat does **not** send just the word you typed.  It wraps your
one-line ask inside a multi-kilobyte XML-like envelope:

```xml
<attachments>...file contents...</attachments>
<context>...editor context...</context>
<reminderInstructions>...standing instructions...</reminderInstructions>
<userRequest>explain async/await in C#</userRequest>
```

If you naively logged the last `user` message you would see ~3 000 characters
of boilerplate instead of `explain async/await in C#`.  `CopilotMessageExtractor`
peels the envelope away so the harness can make cheap, accurate decisions.
It runs a small, deterministic algorithm:

1. **Find the last `user` message.**  Copilot resends the whole conversation on
   every turn; the *last* `user` entry is the current ask.
2. **Read its text — handling both content shapes:** a plain string
   (`"content": "hi"`) or a multi-part array
   (`"content": [{"type":"text","text":"hi"}]` — the extractor concatenates
   every `text` part).
3. **Unwrap the envelope, in priority order:** return the inner text of
   `<userRequest>…</userRequest>` if present; otherwise strip every known
   wrapper block (`attachments`, `context`, `reminderInstructions`,
   `environment_info`, `editorContext`, `currentEditor`, `instructions`,
   `toolResult`, …); if stripping removes everything (a plain `curl` client),
   fall back to the raw message unchanged.

The result is logged as the `[copilot ask]` line. **This log line is the seed of
all intelligence** — once you can see the ask clearly you can route on it, apply
policy, attribute cost, or classify intent. The class is `public static`, so
`CopilotMessageExtractor.GetLastUserMessageText(body)` can be reused verbatim.

### ② b — Resolving the model to an Azure deployment (pass-through vs remap)

This is the key nuance versus the Ollama sample:

- **Ollama** carries the model in the request **body** — so OllamaProxy rewrites
  the body's `"model"` field.
- **Azure OpenAI** carries the deployment in the **URL path**
  (`openai/deployments/{deployment}/chat/completions`) and *ignores* the body
  `model`. So to honor the model the user picked, FoundryProxy uses the requested
  id **as the deployment name when it builds the URL** (`BuildRequestUri`).

For each request it looks at the `"model"` field:

- **A real deployment id → routed to that deployment (pass-through).**  Whatever
  model you picked in the VS Code model picker becomes the deployment in the URL.
  This is what lets one proxy serve `gpt-5.5`, `gpt-chat-latest`, `DeepSeek-V4-Flash`,
  etc. You'll see `[model passthrough] 'gpt-5.5'`.
- **Utility alias / empty / unknown id → routed to the default deployment.**
  VS Code's agent surface sends background "utility" requests with
  `model: "copilot-utility-small"`. The proxy routes those to `Foundry:Deployment`
  (the fallback) so the utility slot works and typos don't 404. You'll see
  `[model rewrite] 'copilot-utility-small' → deployment 'gpt-4o-mini'`.

**Config-driven, not auto-discovered.** The Ollama sample discovers installed
models via `GET /api/tags`. Azure OpenAI has no data-plane "list deployments"
API (that is a control-plane / ARM operation), so you declare the deployments
this proxy may pass through in `Foundry:Deployments` (see *Serving multiple
Foundry deployments* below). Leave it empty to allow **any** requested id to be
used as a deployment name.

### ② c — Stripping unsupported sampling params

Newer Foundry models (`gpt-5.x`, `gpt-chat-latest`, o-series) only accept the
**default** `temperature`/`top_p` of `1` and return a `400 unsupported_value` for
anything else. VS Code Copilot sends `temperature: 0.1` on every call, so when
`Foundry:StripUnsupportedSamplingParams` is `true` (the default) the proxy removes
any non-default `temperature`/`top_p` before forwarding. You'll see
`[param strip] removed 'temperature'=0.1`.

### ② d — Detecting streaming

The OpenAI protocol signals streaming with `"stream": true`.  The proxy reads
that flag while parsing and pipes an `text/event-stream` response straight back
for streaming requests, or returns a single JSON body otherwise. Copilot Chat
always sets `stream: true`; plain `curl` tests often don't.

---

## Register in VS Code as a BYOK model provider

VS Code Copilot supports custom model providers via the
`chatLanguageModels.json` user settings file.  Paste this entry into your
Copilot custom model providers list:

```json
[
  {
    "name": "Microsoft Foundry BYOK Proxy (sample)",
    "vendor": "customendpoint",
    "apiKey": "vscode-provider-placeholder",
    "apiType": "chat-completions",
    "models": [
      {
        "id": "gpt-4o-mini",
        "name": "gpt-4o-mini (Azure Foundry)",
        "url": "http://localhost:5100/v1/chat/completions",
        "toolCalling": true,
        "vision": false,
        "maxInputTokens": 128000,
        "maxOutputTokens": 16000
      },
      {
        "id": "copilot-utility-small",
        "name": "Azure Foundry utility (gpt-4o-mini)",
        "url": "http://localhost:5100/v1/chat/completions",
        "toolCalling": false,
        "vision": false,
        "maxInputTokens": 128000,
        "maxOutputTokens": 2048
      }
    ]
  }
]
```

Replace `"gpt-4o-mini"` with your actual deployment name if it differs.

> **Important: two `apiKey` fields, two very different meanings**
>
> | Field | Where | What it is |
> |---|---|---|
> | `"apiKey": "vscode-provider-placeholder"` | `chatLanguageModels.json` | A **VS Code provider key** — an opaque identifier VS Code uses internally. It is NOT the Azure API key. Any non-empty string works. |
> | `Foundry:ApiKey` in user secrets | Server-side | The **real Azure OpenAI key**. It lives in user secrets on your machine, is added to every outgoing request as the `api-key` HTTP header, and is NEVER visible to VS Code or sent to the client. |
>
> This separation is the central security teaching point of this sample:
> the VS Code client speaks only to `http://localhost:5100` — it never
> knows (or needs to know) the Azure key.

> **Why two model entries?**  
> VS Code's agent surface uses a second "utility" model for background tasks
> (chat titles, commit-message suggestions, rename hints).  When the main
> model is BYOK, VS Code cannot use its built-in utility models and needs a
> registered BYOK model to fill that slot.  See the Troubleshooting section
> below for the full explanation.

After registering, open VS Code Copilot Chat, click the model picker, and
select **gpt-4o-mini** from the custom providers list.

### Serving multiple Foundry deployments

One proxy can front **many** Foundry deployments — parity with the Ollama
sample's install-aware pass-through. List the deployment names you want to expose
in `appsettings.json` (or via env vars):

```jsonc
"Foundry": {
  "Deployment": "gpt-4o-mini",                                  // fallback + utility slot
  "Deployments": [ "gpt-5.5", "gpt-chat-latest", "DeepSeek-V4-Flash" ]
}
```

Then register each id in `chatLanguageModels.json` (all pointing at
`http://localhost:5100/v1/chat/completions`) and pick any of them in VS Code.
The proxy uses the **requested id as the Azure deployment name** in the URL path,
so each selection hits its matching deployment. Anything not in the list — and
the `copilot-utility-small` utility alias — falls back to `Foundry:Deployment`.

> Leave `Foundry:Deployments` empty to allow **any** requested id to be used as a
> deployment name (the utility alias still falls back to `Foundry:Deployment`).
> `GET /v1/models` advertises every configured deployment plus the utility alias.

**For agent mode (the "Describe what to build" surface):** also add the
following two lines to your VS Code `settings.json` so the utility slot is
satisfied:

```json
"chat.utilityModel":      "copilot-utility-small",
"chat.utilitySmallModel": "copilot-utility-small"
```

---

## Incremental showcase

Use this as a live presentation progression — each step is a small win that
motivates building a real harness.

### Step 1 — BYOK to a cloud Foundry model with zero client change ✅ *(what this sample does)*

Start the proxy and ask Copilot a question in VS Code.  Copilot answers using
your **Microsoft Foundry** deployment:

- ✅ Cloud model — GPT-4o-mini, GPT-4o, or any Azure OpenAI deployment  
- ✅ Zero change to the VS Code client — just a model provider URL  
- ✅ Works with any Azure region or custom AI Foundry project endpoint  

### Step 2 — Secrets stay server-side, never in the client or repo ✅ *(what this sample does)*

The Azure API key lives in .NET User Secrets on the server:

```
%APPDATA%\Microsoft\UserSecrets\adc0ec66-b21e-4f58-93a3-14e8049f8b55\secrets.json
```

VS Code only sees `http://localhost:5100` — the `api-key` header is added
by the proxy on every outgoing request and is invisible to the client.
The repo contains only empty placeholders in `appsettings.json`.

### Step 3 — Observe the ask ✅ *(what this sample does)*

Watch the terminal while you type in Copilot Chat:

```
[copilot ask] explain async/await in C#
```

The proxy logs **only the words you typed**, not the kilobytes of Copilot
boilerplate (`<attachments>`, `<context>`, `<reminderInstructions>`, …).
This is the `CopilotMessageExtractor` class at work — it peels away the
XML envelope VS Code wraps around every message.

### Step 4 — Next steps: the full harness routes between Foundry and Ollama *(full harness)*

The **[OllamaProxy](../OllamaProxy/README.md)** sample (port 5099) does the same
thing for a *local* Ollama model.  Together, the two samples illustrate the
two ends of the spectrum:

- **OllamaProxy** → local model, zero cloud cost, fully offline  
- **FoundryProxy** → cloud Foundry model, full GPT-4o capability, user secrets  

The **[ElBruno.CopilotHarness](../../../README.md)** project combines both —
the `Router.Api` receives every Copilot request and routes it to either a
local Ollama model or a Foundry deployment based on configurable policy rules
(topic sensitivity, model capability, cost budget, etc.).  The two samples
you just ran are the building blocks that router is built on.

---

## Troubleshooting

### 401 Unauthorized / 403 Forbidden

The proxy returns a friendly JSON error with a hint:

```json
{"error":"Azure returned 401. Check that Foundry:ApiKey, Foundry:Endpoint, and Foundry:Deployment are correct in user secrets."}
```

Check:
- `Foundry:ApiKey` — copy the key fresh from the Azure portal (Keys and Endpoint blade)
- `Foundry:Endpoint` — must match the resource endpoint exactly
- `Foundry:Deployment` — must match the **deployment name** (not the base model name)

Run `dotnet user-secrets list` to see what is currently stored.

### 404 Not Found from Azure

Usually means the deployment name or API version is wrong:
- Check `Foundry:Deployment` — deployment names are case-sensitive
- Try a different `Foundry:ApiVersion` (e.g. `2024-02-01` or `2025-01-01-preview`)

### "No utility model is configured for 'copilot-utility-small'"

See the explanation in [OllamaProxy/README.md](../OllamaProxy/README.md#no-utility-model-is-configured-for-copilot-utility-small) — the cause and fix are identical.  Summary:

1. Add the `copilot-utility-small` entry to `chatLanguageModels.json` (snippet above).
2. Add to VS Code `settings.json`:
   ```json
   "chat.utilityModel":      "copilot-utility-small",
   "chat.utilitySmallModel": "copilot-utility-small"
   ```

### Utility model note

For Azure, a single deployment handles **both** the main and utility model
slots in this simple proxy.  The deployment is in the URL path — the body
`"model"` field is ignored by Azure.  So `copilot-utility-small` requests
silently hit the same deployment as regular chat requests.  You will see a
`[model rewrite]` log line for each utility call so it is clear on stage.

In production you would configure two deployments (a large model for chat,
a small/fast one for utility tasks) and route by the requested model ID.

---

## Configuration reference

| Setting | Default | How to set |
|---|---|---|
| Azure endpoint | *(required)* | User Secrets: `dotnet user-secrets set "Foundry:Endpoint" "..."` |
| Azure API key | *(required)* | User Secrets: `dotnet user-secrets set "Foundry:ApiKey" "..."` |
| Deployment name | *(required)* | User Secrets: `dotnet user-secrets set "Foundry:Deployment" "..."` — fallback + utility slot |
| Extra deployments | *(none)* | `appsettings.json` → `Foundry.Deployments` array or env `Foundry__Deployments__0`, `__1`… — ids VS Code may pass through to matching Azure deployments |
| API version | `2024-10-21` | `appsettings.json` → `Foundry.ApiVersion` or env `Foundry__ApiVersion` |
| Utility model alias | `copilot-utility-small` | `appsettings.json` → `Foundry.UtilityModelId` or env `Foundry__UtilityModelId` |
| Proxy port | **5100** | Change `app.Run(...)` in `Program.cs` |

---

## Files in this sample

| File | Purpose |
|---|---|
| `Program.cs` | Minimal API — proxy + streaming + Azure auth + user secrets loading |
| `CopilotMessageExtractor.cs` | Extracts the typed user ask from a Copilot Chat payload |
| `appsettings.json` | Non-secret config (ApiVersion, UtilityModelId); empty placeholders for secrets |
| `FoundryProxy.csproj` | net10.0 ASP.NET Core project — no external NuGet packages; UserSecretsId set |
| `.gitignore` | Excludes `bin/` and `obj/` |
| `README.md` | This file |
