# Blog Post Outline: Removing Ollama as a Prerequisite with Foundry Local

**Working title:** "One `winget install` Away: Using Foundry Local as the Routing Brain in CopilotHarness"

**Target audience:** .NET developers, AI/ML engineers, GitHub Copilot power users

**Estimated length:** 1500–2000 words

---

## Outline

### 1. Introduction — The prerequisite problem (200 words)

- CopilotHarness needs a local AI to classify prompts for routing
- Previously: install Ollama, pull llama3.1:8b, configure endpoint
- Pain points: extra steps for contributors, not available on all platforms, NPU hardware wasted

### 2. What is Foundry Local? (250 words)

- Microsoft's on-device model runner
- Ships as a WinGet package (`Microsoft.FoundryLocal`)
- Exposes the same OpenAI `/v1/chat/completions` API as Ollama
- NPU-capable on Copilot+ PCs
- phi-4-mini: 3.8B parameters, ~2.5GB, strong JSON/structured output

**Key quote:** *"Same API, better hardware utilization, one install."*

### 3. The Architecture: Pluggable Processor Model (300 words)

- Explain the routing pipeline: prompt → processor → rule match → cloud model
- Show the `ModelProviderType` enum (`FoundryLocal = 2`, `Ollama`, `AzureOpenAI`)
- Explain `IsLocalProvider()` extension — covers both local types
- Diagram: processor model paths (Mermaid, adapted from `docs/diagrams/`)
- Key insight: any model with `IsProcessor=true` becomes the classifier — no code change needed

### 4. The Implementation: What Changed (300 words)

- `FoundryLocalChatCompletionsProvider` — 35 lines, mirrors OllamaChatCompletionsProvider
- `FoundryLocalOptions` — endpoint + default model config
- Seeding: phi-4-mini seeded as default processor; Ollama demoted to `IsProcessor=false`
- Upgrade migration: idempotent SQL, doesn't break existing databases
- Admin UI: dropdown, provider badge in Live Routing, Setup page card
- Health check: probes `/v1/models` on startup

### 5. Switching Between Providers (250 words)

- Admin UI walkthrough: toggle `IsProcessor` between Foundry Local / Ollama / Azure
- No restart needed — takes effect on the next request
- Screenshot / GIF of the Live Routing provider badge switching
- "The harness doesn't care which provider you pick — the pipeline is identical"

### 6. Fallback and Resilience (150 words)

- If no processor is running: deterministic keyword fallback
- Live Routing shows `deterministic` vs `processor-model` source
- Health check surface: `/health` endpoint reports `foundry-local-endpoint` status
- Copilot+ PC NPU: automatic, no config

### 7. Migration from Ollama (200 words)

- Fresh database: nothing to do — phi-4-mini is the default
- Existing database: flip `IsProcessor` in the Admin UI
- Link to `docs/Migration_Ollama_To_FoundryLocal.md`

### 8. Results and Next Steps (150 words)

- 199 tests pass, 10 new added
- Zero Ollama prerequisite for new contributors
- phi-4-mini uses NPU automatically on supported hardware
- Next: direct SDK integration (no FoundryLocalProxy), multi-processor A/B evaluation

---

## Key code snippets to include

1. `ModelProviderType.cs` — the three enum values + `IsLocalProvider()`
2. `FoundryLocalChatCompletionsProvider` — the ~35-line provider class
3. `appsettings.json` — the `FoundryLocal` section
4. Mermaid diagram from `docs/diagrams/processor-model-architecture.md`

---

## SEO keywords

- Foundry Local .NET
- phi-4-mini routing
- GitHub Copilot BYOK harness
- local AI model classification
- OpenAI-compatible on-device inference
- CopilotHarness processor model
