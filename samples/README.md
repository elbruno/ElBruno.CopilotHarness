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

| Sample | Description |
|---|---|
| [OllamaProxy](OllamaProxy/README.md) | Minimal ASP.NET Core proxy that forwards VS Code Copilot Chat requests to a local Ollama model via the OpenAI-compatible API, with a `CopilotMessageExtractor` class that unwraps the Copilot Chat XML envelope to reveal what the user actually typed. |

---

> **Tip:** Start with `OllamaProxy` — it's the simplest possible version of
> what `ElBruno.CopilotHarness` does, and it runs with a single `dotnet run`.
