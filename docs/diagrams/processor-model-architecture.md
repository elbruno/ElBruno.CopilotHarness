# Processor Model Architecture

## Routing pipeline with pluggable processor model

```mermaid
flowchart LR
    subgraph Client ["VS Code / GitHub Copilot"]
        C[Prompt]
    end

    subgraph Router ["CopilotHarness Router.Api"]
        direction TB
        EP["POST /v1/chat/completions"]
        PM["Processor Model\nClassification Agent"]
        DK["Deterministic\nFallback Classifier"]
        RR["Rule Matcher"]
        GW["Route Gateway"]
    end

    subgraph Processor ["Processor Model (pluggable)"]
        direction TB
        FL["🟦 Foundry Local\nphi-4-mini\n:5101\n(default)"]
        OL["🟢 Ollama\nllama3.1:8b\n:11434"]
        AZ["☁️ Azure OpenAI\ngpt-5-mini\n(cloud)"]
    end

    subgraph Cloud ["Cloud Model"]
        GPT["Azure Foundry\ngpt-5-mini / gpt-5.5"]
    end

    subgraph Local ["Local Tool Caller"]
        PHI["phi-4-mini\n(if tool-capable)"]
    end

    C --> EP
    EP --> PM
    PM -->|"IsProcessor=true"| FL
    PM -->|"IsProcessor=true"| OL
    PM -->|"IsProcessor=true"| AZ
    PM -->|"timeout / error"| DK
    FL --> RR
    OL --> RR
    AZ --> RR
    DK --> RR
    RR -->|"matched rule"| GW
    GW -->|"cloud rule"| GPT
    GW -->|"local rule"| PHI
    GPT --> C
    PHI --> C
```

## Provider type comparison

| | Foundry Local | Ollama | Azure OpenAI |
|---|---|---|---|
| `type` value | `foundry-local` | `ollama` | `azure-openai` |
| Default port | 5101 (proxy) / 55588 (SDK) | 11434 | cloud |
| API key | ❌ None | ❌ None | ✅ Required |
| Default processor model | phi-4-mini | llama3.1:8b | gpt-5-mini |
| NPU-capable | ✅ Yes | ❌ No | N/A |
| Tool-calling | ✅ Yes (phi-4-mini) | ✅ Yes (llama3.1:8b) | ✅ Yes |
| Install | `winget install Microsoft.FoundryLocal` | `winget install Ollama.Ollama` | Azure portal |

## Configuration flexibility

Any model in the registry can become the processor by setting `IsProcessor = true`.
The router's classification path is fully provider-agnostic — the same code runs
regardless of whether the processor is Foundry Local, Ollama, or a cloud GPT.

```
Router.Api/Intelligence/
  ProcessorModelClassificationAgent  ← provider-agnostic orchestrator
    IChatCompletionsProviderFactory  ← picks the right HTTP client
      FoundryLocalChatCompletionsProvider  ← type: foundry-local
      OllamaChatCompletionsProvider        ← type: ollama
      AzureFoundryChatCompletionsProvider  ← type: azure-openai
    DeterministicClassificationAgent ← keyword fallback (no model needed)
```
