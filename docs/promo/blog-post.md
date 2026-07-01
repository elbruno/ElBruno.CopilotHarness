# You Set the Rules for GitHub Copilot — Every Request, Every Model

*Originally published on [elbruno.com](https://elbruno.com)*

---

## The Problem Nobody Talks About

You have GitHub Copilot. It's smart, fast, and woven into your daily workflow. But here's the thing nobody puts on the marketing page: **every prompt you type goes to a model you didn't choose, on infrastructure you don't control, with zero visibility into why that model was picked**.

That's fine for a lot of developers. But as AI-assisted coding moves deeper into enterprise workflows, three friction points start to hurt:

1. **Privacy** — Your team's proprietary code, internal architecture, and unreleased features travel to a cloud endpoint with every tab-completion. For many regulated environments, that's a blocker.

2. **Cost** — A one-word inline completion and a 10,000-token code review cost the same per-token. Local models are essentially free after the hardware investment — but there's no built-in way to route "simple stuff" locally.

3. **Control and explainability** — When something goes wrong — a hallucinated API, a bad refactor — you have no record of which model responded, which version it was, or what context it saw. Routing is a black box.

**ElBruno.CopilotHarness** is an open-source project that puts all three of those concerns back in your hands, without changing a single line of your IDE config beyond the one-time BYOK (Bring Your Own Key) setup.

---

## The One-Liner

> **Route every GitHub Copilot request through your own rules — local for speed and privacy, cloud for power — with full explainability and zero client-side changes.**

---

## How It Works: The 5 Layers

The harness sits between Copilot and the upstream model. Visualize it as a vertical stack, each layer feeding into the next:

### Layer 1 — Copilot (VS Code / Copilot App / CLI)

This is your normal Copilot experience. Nothing changes here. You keep using chat, inline completions, and agent mode exactly as before.

### Layer 2 — BYOK Configuration

GitHub Copilot supports custom endpoints via its *Manage Language Models* flow (VS Code). You point one entry at `http://localhost:5117/v1/chat/completions` — the harness router URL. From this point forward, Copilot's requests flow through your machine first.

The harness even generates the exact config JSON for you: open `http://localhost:5117/connect` after launch, click **Copy config**, and paste into VS Code's `chatLanguageModels.json`. That's the entire setup.

### Layer 3 — The Router (launched with .NET Aspire)

Running `aspire run` in the repo root starts three services together:

- **Router.Api** — the OpenAI-compatible proxy that Copilot talks to. It accepts the same request format Copilot would send to OpenAI and forwards to whatever model the rules select.
- **Admin.Web** — a Blazor dashboard where you manage models, write routing rules, and watch live routing telemetry.
- **Aspire dashboard** — distributed tracing and metrics across all services via OpenTelemetry.

The router is stateless from Copilot's perspective: requests come in, a model is selected, the response comes back — with full HTTP streaming preserved so Copilot's token-by-token rendering keeps working.

### Layer 4 — The Model Selector

This is where the intelligence lives. Two mechanisms work together:

**Rules Engine** — An ordered list of condition-based rules you write and tune. Conditions include: prompt keyword/regex match, prompt size, streaming mode, system-message presence, and *intent* (see below). The first matching rule wins. A first-run wizard seeds sensible defaults; there's also a built-in rule tester.

**AI Classifier (Processor Model)** — Before rules are evaluated, an on-device AI model (by default, Ollama `llama3.1:8b`) reads the first ~200 characters of the prompt and classifies the intent: `simple-chat`, `code-task`, `github-actions`, `launch-app`, or `long-form`. Rules can match on this intent. If the classifier is unavailable, a deterministic keyword fallback takes over — routing never breaks.

Every decision is logged with a plain-language explanation: *"processor 'ollama llama3.1' classified intent=code-task (0.89) → rule 'Code tasks' matched → routed to 'foundry gpt-5-mini'."*

There's also a **size-aware tool guard**: large agentic (tool-calling) payloads automatically go to a cloud model that can handle them, even if a local rule would otherwise route locally. This fixes the "Response too long" error that VS Code throws when a local model over-generates on a huge working set.

### Layer 5 — Local LLM or Azure OpenAI

Two model connections are seeded out of the box:

- **`ollama llama3.1`** — `llama3.1:8b` running locally via Ollama. Free, private, fast for short prompts. Also the default AI classifier.
- **`foundry gpt-5-mini`** — Azure AI Foundry / `gpt-5-mini`. Handles complex, large, or agentic requests.

You can register as many model connections as you need. Each connection has its own endpoint, API key (encrypted at rest via ASP.NET Core Data Protection), and capability flags (`supportsToolCalling`, `supportsCustomTemperature`). Rules reference models by name — changing the physical endpoint behind a name doesn't require touching any rule.

---

## Why It Matters Beyond the Demo

**Privacy by default.** The local path is the default path. Short prompts, quick questions, inline completions — these never leave your machine. Only when a rule says "this needs cloud" does a request go upstream.

**Measurable cost reduction.** When your team's simple-chat and local code-review prompts stay on a machine you already run, the token bill drops. The Admin dashboard shows per-model request volume so you can see the split.

**Explainability for audits.** Every routing decision is a persisted trace: which model, which rule, which classifier output, which tokens consumed, what the upstream HTTP status was. The **AI Judge** can replay those traces and benchmark model quality over time, so you have data for governance conversations.

**Zero lock-in.** The router speaks standard OpenAI API. Swap out models, add a new provider, change rules — none of it requires a Copilot update or an IDE restart.

---

## Getting Started in Three Steps

**Prerequisites:** .NET 10 SDK, Aspire CLI (`dotnet tool install --global aspire`), Ollama with `llama3.1:8b` pulled, a GitHub Copilot subscription, and an Azure AI Foundry endpoint.

```powershell
# 1 — Set your secrets once
cd src/ElBruno.CopilotHarness.AppHost
aspire secret set FoundryEndpoint "https://<your-resource>.openai.azure.com/openai/v1"
aspire secret set FoundryApiKey   "<your-azure-foundry-api-key>"
aspire secret set AdminApiKey     "<any-password-you-choose>"

# 2 — Launch
aspire run

# 3 — In VS Code: Manage Language Models → Add → Custom Endpoint
# URL: http://localhost:5117/v1/chat/completions
```

Open the Admin dashboard, watch the Live Routing feed, and write your first rule. The whole thing takes under 10 minutes from clone to first routed Copilot request.

---

## What's Next

The project is actively developed. Recent additions include tool-calling capability guards (so agentic Copilot mode works reliably), a routing footer injected into the Copilot chat window (great for live demos), continuous evaluation with an AI Judge, and a VS Code extension that shows the last routed model in the status bar.

The roadmap has more in store — shadow routing, per-rule cost analytics, and server-sent events for real-time telemetry push.

---

**Try it:**  
[github.com/elbruno/ElBruno.CopilotHarness](https://github.com/elbruno/ElBruno.CopilotHarness)

**Follow the author:**  
Blog: [elbruno.com](https://elbruno.com) · YouTube: [youtube.com/elbruno](https://www.youtube.com/elbruno) · X: [@elbruno](https://www.x.com/elbruno) · LinkedIn: [@elbruno](https://www.linkedin.com/in/elbruno/)
