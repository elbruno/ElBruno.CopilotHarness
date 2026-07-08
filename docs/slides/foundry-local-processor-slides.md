# Slide Deck: Foundry Local as the Default Routing Processor

## Slide 1 — Title

**CopilotHarness × Foundry Local**
*Removing Ollama as a prerequisite*

Bruno Capuano · @elbruno

---

## Slide 2 — The Problem

> "To test routing rules, you need to install Ollama and pull llama3.1:8b first."

- Extra prerequisite friction for new contributors
- Ollama not available on all machines (Linux only, or no GPU)
- NPU-capable devices (Copilot+ PCs) left out

---

## Slide 3 — The Solution: Foundry Local

Microsoft Foundry Local = **on-device AI** via a standard OpenAI-compatible REST API

| Foundry Local | Ollama |
|---|---|
| `winget install Microsoft.FoundryLocal` | Separate installer |
| NPU-capable (Copilot+ PCs) | CPU/GPU only |
| phi-4-mini built in | Pull required |
| Same `/v1/chat/completions` API | Same API |

---

## Slide 4 — Architecture: Pluggable Processor

```
User prompt
    ↓
[Router.Api] → [Processor Model] → Rule match → Cloud model
                    ↑
          Any of these (IsProcessor=true):
          🟦 Foundry Local phi-4-mini  ← default
          🟢 Ollama llama3.1:8b
          ☁️  Azure GPT (cloud)
```

The processor is just a flag — swap it any time, no restart needed.

---

## Slide 5 — What We Added

| Component | Change |
|---|---|
| `ModelProviderType` | Added `FoundryLocal = 2` |
| `FoundryLocalChatCompletionsProvider` | New — mirrors Ollama provider |
| `IsLocalProvider()` extension | Covers both Ollama + Foundry Local |
| phi-4-mini seed | Default processor on fresh DB |
| Health check | Probes `/v1/models` endpoint |
| Admin UI | Dropdown, badge, Setup page card |
| Live Routing | Provider type badge in pipeline view |
| Tests | +10 new (health check, flexibility, all provider types) |
| Docs | Setup guide, migration guide, troubleshooting |

---

## Slide 6 — Zero Ollama Dependency

Fresh start on a Copilot+ PC:

```bash
winget install Microsoft.FoundryLocal
foundry model run phi-4-mini   # downloads ~2.5GB
# → router starts, phi-4-mini classifies, routing works
```

No Ollama, no Docker, no GPU required.

---

## Slide 7 — Configuration Flexibility

Old: one hardcoded local model (Ollama llama3.1)
New: any model can be the processor

```json
// appsettings.json
"FoundryLocal": {
  "Endpoint": "http://localhost:5101",
  "DefaultModel": "phi-4-mini"
}
```

Admin UI → Models → toggle `IsProcessor` — takes effect immediately.

---

## Slide 8 — Live Demo Flow

1. Start harness (Foundry Local running)
2. Setup page → shows phi-4-mini as processor (Foundry Local badge)
3. Send a message in Copilot Chat
4. Live Routing → processor stage shows `🧠 phi-4-mini · Foundry Local`
5. Switch IsProcessor to Ollama → send another message
6. Live Routing → now shows `🧠 llama3.1:8b · Ollama`

---

## Slide 9 — Results

- ✅ 199 tests pass (10 new)
- ✅ phi-4-mini seeds as default processor
- ✅ Ollama support unchanged (opt-in)
- ✅ Azure GPT as processor also works
- ✅ Live Routing shows provider type badge
- ✅ Health check added for Foundry Local endpoint

---

## Slide 10 — Next Steps

- [ ] Direct Foundry Local SDK integration (no proxy needed)
- [ ] Multi-processor support (A/B evaluation)
- [ ] Model download progress in Admin UI
- [ ] Benchmark dashboard: Foundry Local vs Ollama accuracy

---

*Demo script: `docs/demos/foundry-local-processor-demo.md`*
