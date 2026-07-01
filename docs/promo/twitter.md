# X / Twitter Thread

---

**Tweet 1 (hook)**
Every GitHub Copilot prompt goes to a model you didn't choose, on infra you don't control, with zero visibility into why.

I built a harness that fixes all three of those. 🧵

---

**Tweet 2**
ElBruno.CopilotHarness is a local-first BYOK router for GitHub Copilot.

One `aspire run`, one custom endpoint in VS Code → every Copilot request now flows through YOUR rules before it reaches any model.

github.com/elbruno/ElBruno.CopilotHarness

---

**Tweet 3**
The 5 layers (top → bottom):

1️⃣ Copilot (VS Code / CLI / App) — unchanged
2️⃣ BYOK config — one custom endpoint
3️⃣ Router running via .NET Aspire
4️⃣ Model selector (rules engine + AI classifier)
5️⃣ Local LLM (Ollama llama3.1:8b) OR Azure OpenAI

---

**Tweet 4**
The model selector is the interesting bit.

An on-device AI model reads the first ~200 chars of each prompt, classifies intent (simple-chat / code-task / github-actions / long-form), and a rules engine picks the right model.

Every decision logged. Full explainability.

---

**Tweet 5**
Practical result:

🔒 Short prompts → local Ollama (private, free, fast)
⚡ Complex tasks → Azure OpenAI / gpt-5-mini
🛠 Agentic tool-calling requests → auto-routed to a tool-capable model

And the Admin dashboard shows you every routing decision in real time.

---

**Tweet 6**
There's even a routing footer injected into the Copilot chat window for demos:

```
🧭 Harness · rule 'Simple chat' → ollama llama3.1 · 45 tok
```

Flip it on/off live without restarting. Great for talks and screen recordings.

---

**Tweet 7**
Stack: .NET 10 · .NET Aspire · Blazor · EF Core · SQLite · Microsoft.Extensions.AI · OpenTelemetry

Tests passing. MIT license. Open source.

If you're thinking about Copilot governance, local LLM routing, or AI explainability in enterprise — this is for you.

👉 github.com/elbruno/ElBruno.CopilotHarness

#GitHubCopilot #DotNet #LocalAI #AIRouting

---

**Tweet 8 (closing CTA)**
If this is useful, give it a ⭐ and share with your team.

More features incoming: shadow routing, per-rule cost analytics, real-time telemetry push.

Follow @elbruno for updates 🧡
