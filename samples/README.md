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

The two samples are **complementary**:

- **OllamaProxy** → local model, zero cloud cost, fully offline — great for privacy-first demos.
- **FoundryProxy** → cloud Foundry model via user secrets — great for showing BYOK against a GPT-4o-class model.

Both can run simultaneously (different ports).  The full **[ElBruno.CopilotHarness](../README.md)** router
receives every Copilot request and routes it to *either* a local Ollama model *or* an Azure Foundry
deployment based on configurable policy rules — the two samples are the building blocks that router
is built on.

---

> **Tip:** Start with `OllamaProxy` — it's the simplest possible version of
> what `ElBruno.CopilotHarness` does, and it runs with a single `dotnet run`.
> Then try `FoundryProxy` to see the same pattern applied to a cloud provider
> with proper secret management.
