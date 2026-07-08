# Processor Model Setup Guide

The CopilotHarness routing rules engine uses a **processor model** — a local or cloud AI that reads
each incoming prompt and decides which routing rule applies. This guide explains your options and how
to switch between them.

## Overview

```
User prompt → Processor model → Best rule match → Route to cloud model
                 (local or cloud)
```

The processor model is selected by setting `IsProcessor = true` on exactly one model in the
**Model Registry** (`/models`). Any model type can be the processor — there is no hard dependency on
any particular provider.

---

## Option A — Microsoft Foundry Local (Default)

**phi-4-mini** running locally via [Foundry Local](https://learn.microsoft.com/azure/foundry-local).

| | |
|---|---|
| Model | `phi-4-mini` |
| Provider | Foundry Local / FoundryLocalProxy |
| Default endpoint | `http://localhost:5101` |
| Prerequisite | Install Foundry Local CLI or run FoundryLocalProxy |
| API key required | ❌ No |

### Setup

1. Install the Foundry Local CLI:
   ```
   winget install Microsoft.FoundryLocal
   ```
2. Pull phi-4-mini:
   ```
   foundry model run phi-4-mini
   ```
3. Optionally run the FoundryLocalProxy (port 5101) from `samples/FoundryLocalProxy`:
   ```
   cd samples/FoundryLocalProxy
   dotnet run
   ```
4. The harness seeds `foundry local phi-4-mini` as the default processor on a fresh database.
   No admin configuration is needed.

---

## Option B — Ollama

Any Ollama model can serve as the processor. Recommended: `llama3.1:8b` (clean tool-calls).

| | |
|---|---|
| Model | `llama3.1:8b` (or any Ollama-hosted model) |
| Provider | Ollama |
| Default endpoint | `http://localhost:11434` |
| Prerequisite | [Install Ollama](https://ollama.com/download) + `ollama pull llama3.1` |
| API key required | ❌ No |

### Setup

1. Add an Ollama model in the Model Registry (`/models`):
   - **Type**: `ollama`
   - **Endpoint**: `http://localhost:11434`
   - **Model name**: `llama3.1:8b`
2. Enable **Is Processor** for that model.
3. Disable **Is Processor** on the Foundry Local model (only one processor is active at a time).

---

## Option C — Azure / Foundry Cloud GPT

A cloud-hosted GPT model (e.g., `gpt-5-mini`) as the processor. Useful for highest accuracy with
no local hardware requirement.

| | |
|---|---|
| Model | Any Azure Foundry deployment (e.g., `gpt-5-mini`) |
| Provider | Azure OpenAI |
| Prerequisite | Azure subscription + model deployment |
| API key required | ✅ Yes (or managed identity) |

### Setup

1. Add an Azure model in the Model Registry with `azure-openai` type and your endpoint/API key.
2. Enable **Is Processor** for that model and disable it on all others.

> ⚠️ **Temperature note**: GPT models with `SupportsCustomTemperature = false` (e.g., o-series, gpt-5.x)
> have their `temperature` and `top_p` parameters stripped before forwarding — this happens
> automatically when the flag is set correctly in the Model Registry.

---

## Switching the Processor Model

1. Open **Model Registry** (`/admin` → Models).
2. Edit the model you want to promote: enable **Is Processor**.
3. Edit the current processor: disable **Is Processor**.
4. Navigate to **Setup** (`/setup`) to confirm the change in the "Processor Model" card.

The change takes effect immediately — no restart is needed.

---

## Disabling the Processor (Heuristic-only Mode)

If no model has `IsProcessor = true` and `Enabled = true`, the harness falls back to keyword
heuristics for rule matching. All rules are evaluated by simple pattern matching instead of AI.
This is useful for debugging or when no local AI is available.

---

## Troubleshooting

See [Troubleshooting.md](./Troubleshooting.md#foundry-local) for common issues.
