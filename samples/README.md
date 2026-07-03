# Samples

Teaching samples that each distil **one idea** from the
[ElBruno.CopilotHarness](../README.md) project.

Each sample is a **standalone** project — it is intentionally not added to
`ElBruno.CopilotHarness.slnx` so it cannot break the main solution build.
These samples are designed to be read on stage; every non-obvious line is
commented.

The full harness combines all these ideas — routing, policies, telemetry,
semantic intent, admin UI — in a production-ready Aspire application.

---

## Sample index

| Sample | Port | Description |
|---|---|---|
| [OllamaProxy](OllamaProxy/README.md) | 5099 | Minimal ASP.NET Core proxy that forwards VS Code Copilot Chat requests to a **local Ollama model** via the OpenAI-compatible API, with a `CopilotMessageExtractor` class that unwraps the Copilot Chat XML envelope to reveal what the user actually typed. |
| [FoundryProxy](FoundryProxy/README.md) | 5100 | Minimal ASP.NET Core proxy that forwards VS Code Copilot Chat requests to an **Microsoft Foundry / Azure OpenAI** deployment. Uses **.NET User Secrets** for the endpoint, API key, and deployment name — credentials never touch the VS Code client or the repo. |
| [FoundryLocalProxy](FoundryLocalProxy/README.md) | 5101 | Minimal ASP.NET Core proxy that forwards VS Code Copilot Chat requests to a **Microsoft Foundry Local** instance (phi-4-mini, Llama, Mistral, etc.) running fully offline. Like OllamaProxy but using Foundry Local's standard OpenAI `/v1/models` endpoint — no API key, no cloud, NPU-capable. |

The three samples are **complementary** — all three can run simultaneously:

- **OllamaProxy** (5099) → local model via Ollama, zero cloud cost, fully offline.
- **FoundryProxy** (5100) → cloud model via Azure Foundry — BYOK with proper secret management.
- **FoundryLocalProxy** (5101) → local model via Foundry Local, standard OpenAI API, NPU-capable — ideal as the **sub-agent backend** in the [Agents Architecture](../docs/Agents_Architecture.md).

The full **[ElBruno.CopilotHarness](../README.md)** router combines all three approaches,
routing each Copilot request to the right provider based on configurable policy rules.

---

> **Tip:** Start with `OllamaProxy` — it's the simplest possible version of
> what `ElBruno.CopilotHarness` does, and it runs with a single `dotnet run`.
> Try `FoundryLocalProxy` next for the Foundry Local experience (cleaner API, NPU support).
> Then try `FoundryProxy` to see the same pattern applied to a cloud provider
> with proper secret management.
