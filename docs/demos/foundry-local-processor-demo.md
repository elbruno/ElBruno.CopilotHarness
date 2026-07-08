# Demo Script: Foundry Local as Processor Model

**Duration:** ~8 minutes  
**Audience:** Developers, AI/ML engineers  
**Prerequisites:** CopilotHarness running, Foundry Local installed, phi-4-mini downloaded

---

## Setup (before demo)

```bash
# Terminal 1: Start Foundry Local proxy
cd samples/FoundryLocalProxy
dotnet run

# Terminal 2: Start harness
cd src/ElBruno.CopilotHarness.AppHost
dotnet run
```

Open in browser:
- Admin UI: `http://localhost:5227` (or Aspire dashboard port)
- VS Code with Copilot Chat configured to route through the harness

---

## Scene 1 — Show the Setup page (1 min)

> "Let's start on the Setup page."

Navigate to **Setup** (`/setup`).

Point to the **Processor Model** card:
- Name: `foundry local phi-4-mini`
- Badge: `Foundry Local`
- Endpoint: `http://localhost:5101`

> "This is our local AI classifier. It runs on your machine — no cloud call needed to
> decide which routing rule applies."

---

## Scene 2 — Send a message and watch it classify (2 min)

Open VS Code Copilot Chat. Type:
```
hi there
```

Switch to Admin UI → **Live Routing** (`/live-routing`).

> "Watch the pipeline card. The processor stage shows phi-4-mini — Foundry Local badge —
> and a confidence score. It classified this as `simple-chat`, so it routed to the local model."

Point to:
- `🧠 Decided by local model`
- `phi-4-mini · 0.95`
- `Foundry Local` badge (blue)

---

## Scene 3 — Switch to Ollama (1.5 min)

> "Now let's show the flexibility. I'll swap the processor to Ollama in the Admin UI."

Navigate to **Models** (`/models`).
1. Find `foundry local phi-4-mini` → Edit → uncheck **Is Processor** → Save.
2. Find `ollama llama3.1` → Edit → check **Is Processor** → Save.

Back to **Setup** — processor card now shows `ollama llama3.1 · Ollama`.

Send another message in Copilot Chat.

Back to **Live Routing** — the processor stage now shows:
- `🧠 llama3.1:8b · 0.88`
- `Ollama` badge (green)

> "Same pipeline, different classifier. No restart, no config file change — just a
> toggle in the Admin UI."

---

## Scene 4 — Switch back to Foundry Local (30 sec)

Reverse the toggle. Live Routing shows the Foundry Local badge again.

> "Now Ollama is optional — contributors without it can still use the full routing experience."

---

## Scene 5 — Health check (1 min)

Navigate to `http://localhost:5117/health` (or Aspire dashboard).

Point to:
- `foundry-local-endpoint: Healthy`

Stop FoundryLocalProxy (Ctrl+C in terminal 1).

Refresh `/health`:
- `foundry-local-endpoint: Unhealthy`

Back to Live Routing — classifier source falls back to `deterministic` (keyword fallback).

> "The harness degrades gracefully. If Foundry Local isn't running, keyword heuristics
> take over — routing still works, just less accurately."

Restart FoundryLocalProxy — health check recovers.

---

## Scene 6 — Code tour (1.5 min, optional)

Open `ChatCompletionsProvider.cs`:
- Show `FoundryLocalChatCompletionsProvider` — 35 lines, structurally identical to Ollama.
- Show `ModelProviderType.FoundryLocal = 2` and `IsLocalProvider()`.

> "Adding a new provider is about 35 lines of code. The rest of the system — routing,
> telemetry, tool-calling guard — all use `IsLocalProvider()` which covers both Ollama
> and Foundry Local automatically."

---

## Wrap-up (30 sec)

> "To summarize:
> - Foundry Local is now the default processor — no Ollama prerequisite.
> - Any model can be the processor — toggle it in the UI.
> - Live Routing shows you which provider is running classification.
> - The harness falls back gracefully when no processor is available."

---

## FAQ

**Q: Does phi-4-mini use the NPU on Copilot+ PCs?**  
A: Yes — Foundry Local automatically uses the NPU when available. No extra configuration.

**Q: Can I run Foundry Local without the proxy?**  
A: Yes — point `FoundryLocal:Endpoint` at the SDK's direct port (default 55588).

**Q: What if I don't want a local processor at all?**  
A: Set `IsProcessor = false` on all models. The harness falls back to keyword heuristics.
