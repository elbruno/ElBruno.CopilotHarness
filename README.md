# ElBruno.CopilotHarness

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Aspire](https://img.shields.io/badge/Aspire-13.4-blueviolet?logo=microsoft)](https://aspire.dev)
[![License MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()
[![Tests](https://img.shields.io/badge/tests-58%20passing-brightgreen)]()

> **Intelligent BYOK harness for GitHub Copilot** — built with .NET 10, .NET Aspire, and Microsoft Agent Framework.

Route every GitHub Copilot request through your own infrastructure. Choose which model handles each request, inspect every decision, benchmark quality over time, and enforce rules — all without touching your IDE.

---

## What it does

| Feature | Description |
|---|---|
| **OpenAI-compatible router** | Drop-in proxy for GitHub Copilot BYOK — no client-side changes needed |
| **Intelligent model routing** | Rules + AI agents select the best model per request |
| **Admin dashboard** | Manage models, rules, routing history, and approval workflows |
| **AI Judge** | Replay prompts, benchmark models, score quality with an AI evaluator |
| **Continuous evaluation** | Shadow routing, rule confidence scoring, human approval before changes apply |
| **VS Code extension** | Explain routing decisions and open the dashboard from inside VS Code |
| **OpenTelemetry** | Full distributed tracing across all services via the Aspire dashboard |

---

## Screenshots

> 📸 **Screenshots coming soon — want to contribute one?** See below.

The following screenshots are planned. Each one shows a key part of the harness in action:

| Screenshot | What it shows | Target file |
|---|---|---|
| Aspire Dashboard | All services running with health status and OpenTelemetry traces | `docs/images/aspire-dashboard.png` |
| Admin — Live Routing Dashboard | Connected Copilot clients, live request stream, and model selections | `docs/images/admin-dashboard.png` |
| Admin — Rules Editor | Rule list, priority order, and the inline rule editor | `docs/images/admin-rules.png` |
| Admin — Approval Workflow | AI-recommended rule changes queued for human review | `docs/images/admin-approvals.png` |
| Judge — Benchmark Results | Side-by-side model scores with AI Judge commentary | `docs/images/judge-benchmarks.png` |

### How to contribute a screenshot

1. Run the harness: `aspire run`
2. Navigate to the relevant UI in your browser.
3. Take a screenshot and save it as a PNG to `docs/images/<filename>.png` (match the target filename above).
4. Open a PR — no code changes needed, just the image. Every screenshot makes the project more accessible to new contributors!

---

## Fast start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Aspire CLI](https://aspire.dev) — `dotnet tool install --global aspire`
- A GitHub Copilot subscription
- An Azure AI Foundry endpoint + API key (for upstream model calls)

### 1 — Run the system

```powershell
aspire run
```

### 2 — Configure secrets

When prompted by Aspire, provide:

| Parameter | Description |
|---|---|
| `FoundryEndpoint` | Your Azure AI Foundry base URL |
| `FoundryApiKey` | Your Azure AI Foundry API key |
| `AdminApiKey` | Optional bearer token for admin endpoints |

### 3 — Set up BYOK in GitHub Copilot

1. Open the Aspire dashboard and copy the **Router.Api** URL (e.g. `https://localhost:7xxx`).
2. In VS Code, open Settings → **GitHub Copilot** → **Advanced** → **Custom endpoint**.
3. Set the endpoint to `https://localhost:7xxx/v1` and leave the model as `gpt-5-mini`.
4. Copilot requests will now route through your harness.

---

## Documentation

| Doc | Description |
|---|---|
| [User Manual](docs/User_Manual.md) | Full setup and feature walkthrough |
| [Architecture](docs/Architecture.md) | System design and component boundaries |
| [API Reference](docs/API_Reference.md) | All router and admin endpoints |
| [Current Progress](docs/Current_Progress.md) | Phase status and what's implemented |
| [Troubleshooting](docs/Troubleshooting.md) | Common issues and fixes |
| [Runbook](docs/Runbook.md) | Start, stop, reset, and inspect |
| [Contributing](docs/Contributing.md) | Local dev setup and PR expectations |
| [Docs Index](docs/Docs_Index.md) | Full docs index |

---

## Validate

```powershell
dotnet test .\ElBruno.CopilotHarness.slnx
```
