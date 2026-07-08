# Migration: Ollama → Foundry Local (Processor Model)

This guide walks you through replacing Ollama (`llama3.1:8b`) with Microsoft Foundry Local
(`phi-4-mini`) as the routing rules engine processor.

> **Why migrate?** Foundry Local ships with phi-4-mini which delivers comparable rule-matching
> accuracy to llama3.1:8b with no separate service installation — just `winget install`.

---

## Prerequisites

- CopilotHarness `feature/foundry-local-provider` branch (or later)
- Windows with Foundry Local CLI installed:
  ```
  winget install Microsoft.FoundryLocal
  ```

---

## Step 1 — Start Foundry Local

Pull phi-4-mini and start it (or use FoundryLocalProxy):

```
# Via Foundry Local CLI
foundry model run phi-4-mini

# OR via FoundryLocalProxy (keeps it running at http://localhost:5101)
cd samples/FoundryLocalProxy && dotnet run
```

Confirm it's reachable:
```
curl http://localhost:5101/v1/models
```

---

## Step 2 — Update the Model Registry

### Fresh database

Nothing to do — phi-4-mini is seeded as the default processor automatically.

### Existing database

1. Open **Model Registry** (`/models`) in the Admin UI.
2. Find `foundry local phi-4-mini` — it was inserted during the upgrade.
   - If it's missing, click **Add model**: Type=`foundry-local`, Endpoint=`http://localhost:5101`,
     Model name=`phi-4-mini`, enable **Is Processor**.
3. Find `ollama llama3.1` (or your existing Ollama processor).
4. Edit it → uncheck **Is Processor** → Save.

---

## Step 3 — Verify

1. Open **Setup** (`/setup`) — the Processor Model card should now show `foundry local phi-4-mini · Foundry Local`.
2. Open **Live Routing** (`/live-routing`) and send a test message.
   - The classifier stage should show `🧠 Decided by local model` with a `Foundry Local` badge.

---

## Rollback

To revert to Ollama at any time:

1. Model Registry → find the Ollama model → enable **Is Processor**.
2. Model Registry → find the Foundry Local model → disable **Is Processor**.

Ollama must be running at its configured endpoint.

---

## Keeping Both (A/B switching)

Both models can coexist in the registry; only the one with `IsProcessor=true` is active.
Toggle between them any time without restarting the harness.
