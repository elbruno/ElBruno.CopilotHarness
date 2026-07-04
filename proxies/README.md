# Local Proxy Samples

Three minimal ASP.NET Core proxies that each let VS Code Copilot treat a different
model provider as a BYOK (Bring Your Own Model) endpoint — no modification to Copilot,
no cloud account required (for the local ones).

Each proxy is a **standalone** project — intentionally not added to
`ElBruno.CopilotHarness.slnx` so it cannot break the main solution build.
Every non-obvious line is commented for stage readability.

---

## Quick start — single proxy

```bash
cd proxies/OllamaProxy      # or FoundryProxy, FoundryLocalProxy
dotnet run
```

## Quick start — all three via Aspire

```bash
cd proxies
aspire start
```

Opens the Aspire dashboard with live logs, health, and request timings for all
three proxies — plus the **ProxiesTestApp** web UI — in one view.

> **Requires the Aspire CLI** (`dotnet workload install aspire`).  
> The `aspire.config.json` in this folder already points at `AppHost/AppHost.csproj`,
> so `aspire start` (and `aspire stop`) work from the `proxies/` directory without
> any extra flags.

---

## Proxy index

| Proxy | Port | Backend | Description |
|---|---|---|---|
| [OllamaProxy](OllamaProxy/README.md) | 5099 | Local Ollama | Forwards VS Code Copilot Chat to a local Ollama model via the OpenAI-compatible API. |
| [FoundryProxy](FoundryProxy/README.md) | 5100 | Azure OpenAI / Foundry cloud | Forwards to a cloud Foundry deployment. Uses `.NET User Secrets` — credentials never touch the repo. |
| [FoundryLocalProxy](FoundryLocalProxy/README.md) | 5101 | Foundry Local (offline) | Forwards to Microsoft Foundry Local (phi-4-mini, Llama, etc.) via the C# SDK. Fully offline, NPU-capable. |

All three can run simultaneously. The Aspire AppHost (`AppHost/`) launches all three together.

---

## Test client — ProxiesTestApp (Blazor)

`ProxiesTestApp/` is a Blazor Server web app that tests all three proxies from a browser UI.
It launches automatically when you use `aspire start` (see above).

| Page | Path | What it does |
|---|---|---|
| Health Dashboard | `/` | Live status cards for all three proxies, auto-refreshes every 5 s |
| Chat | `/chat` | Streaming chat — choose proxy, model, and system prompt |
| Compare | `/compare` | Same prompt sent to all three proxies simultaneously, side-by-side |

To run the test app standalone (without Aspire):

```bash
cd proxies/ProxiesTestApp
dotnet run
# opens at http://localhost:5102
```

---

> **New here?** Start with `OllamaProxy` — it's the simplest possible version of
> what `ElBruno.CopilotHarness` does and runs with a single `dotnet run` (plus Ollama).
> Try `FoundryLocalProxy` next for Foundry Local / NPU support.
> Then `FoundryProxy` to see the same pattern applied to a cloud provider with
> proper secret management.
>
> The full **[ElBruno.CopilotHarness](../README.md)** router combines all three approaches,
> routing each Copilot request to the right provider based on configurable policy rules.
