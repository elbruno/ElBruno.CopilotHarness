# LinkedIn Post

---

🤖 **What if you could write the rules for every GitHub Copilot request?**

Route it local for privacy. Route it to the cloud for power. See exactly why every decision was made — in real time.

That's what **ElBruno.CopilotHarness** does.

It's a local-first BYOK harness that sits between GitHub Copilot and your LLMs. One `aspire run` command, one custom endpoint in VS Code, and you're in control:

🔒 **Privacy** — short prompts, quick questions, and inline completions stay on your machine (Ollama `llama3.1:8b`, completely local and free)

💰 **Cost** — complex code tasks and agentic requests go to Azure OpenAI / `gpt-5-mini` only when they need to

🔎 **Explainability** — every routing decision logged with a plain-language reason: *"classifier → rule 'Simple chat' matched → ollama llama3.1"*

⚖️ **Control** — a condition-based rules engine you write: match on intent, prompt size, keywords, regex, streaming mode. First rule wins.

The whole stack runs on **.NET 10 + .NET Aspire** — Router.Api, Admin.Web dashboard, and OpenTelemetry tracing, all in one `aspire run`. There's even a routing footer injected into the Copilot chat window (great for live demos):

```
🧭 Harness · rule 'Simple chat' → ollama llama3.1 · 45 tok
```

And if you want to go further: an AI Judge that benchmarks model quality across your real Copilot history, a VS Code extension that shows the routed model in the status bar, and continuous evaluation with approval workflows.

Zero client-side changes. Open source. MIT license.

👉 **github.com/elbruno/ElBruno.CopilotHarness**

If you're working on AI-assisted developer tools or enterprise Copilot governance, I'd love to hear what routing problems you're running into. Drop a comment 👇

#GitHubCopilot #AIEngineering #DotNet #LocalAI #DeveloperTools
