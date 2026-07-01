# 5-Minute Pitch: ElBruno.CopilotHarness
## Video/Talk Script with Timestamps

**Core message:**  
> Route every GitHub Copilot request through your own rules — local for speed and privacy, cloud for power — with full explainability and zero client-side changes.

**Target audience:** Developers and engineering leads who use GitHub Copilot and want more control over AI model routing.

**Format:** Screencast or live-demo talk. One presenter. Slides optional — the live dashboard is the visual.

---

## SHOT LIST / SCREEN GUIDE

| Timestamp | What's on screen |
|-----------|-----------------|
| 0:00–0:30 | Title card or `docs/presentation/harness-layers.html` — title slide |
| 0:30–1:00 | Layers diagram, slide 1, building layer by layer |
| 1:00–2:00 | Terminal: `aspire run` → Aspire dashboard showing all services healthy |
| 2:00–3:00 | Admin.Web Live Routing feed — real Copilot prompts arriving, model selection visible |
| 3:00–4:00 | Admin.Web Rules editor — show a rule, run the rule tester |
| 4:00–4:30 | VS Code — Copilot chat with routing footer visible in the reply |
| 4:30–5:00 | Closing slide / GitHub URL |

---

## FULL SCRIPT

---

### [0:00–0:30] — Hook

*[Show: title card or presentation title slide]*

Hey — I want to show you something you can set up in under 10 minutes that gives you **complete control over where your GitHub Copilot requests go**.

Right now, every prompt you type in Copilot goes to a model you didn't choose, on infrastructure you don't control. No visibility. No rules. No local option.

This project changes that.

---

### [0:30–1:00] — The 5 Layers

*[Show: the animated layer diagram from `harness-layers.html`, advancing one layer at a time]*

Think of it as a **5-layer stack**.

At the top: **Copilot** — VS Code, Copilot App, CLI. Your normal workflow, untouched.

Below that: **BYOK configuration** — one custom endpoint in VS Code's model settings, pointing at your local router.

Then: the **harness router**, running on your machine via .NET Aspire. One command: `aspire run`. That spins up the router, the admin dashboard, and distributed tracing — all in one shot.

Inside the router: the **model selector** — a rules engine plus an AI classifier that reads each prompt, assigns an intent, and picks the right model.

And at the bottom: your **models** — a local Ollama `llama3.1:8b` for fast, private, free inference, and Azure OpenAI / gpt-5-mini for complex or large requests.

---

### [1:00–2:00] — Launch it live

*[Show: terminal window, type `aspire run` and hit enter → Aspire dashboard opens]*

Let me show you live. I'm in the repo root. `aspire run`.

*[Wait for Aspire dashboard to show all green services]*

You can see the Aspire dashboard — Router.Api, Admin.Web, Evaluation Worker, Judge Web — all healthy. OpenTelemetry distributed traces flowing in real time.

I'll open the Admin dashboard... *[open browser to Admin.Web]* — and flip to **Live Routing**.

---

### [2:00–3:00] — Watch a real Copilot request

*[Show: Admin.Web Live Routing page — auto-refreshing every 2 seconds]*

Now I'll ask Copilot something simple in VS Code.

*[Switch to VS Code, type "explain what a record type is in C#" in Copilot chat, send it]*

*[Switch back to Admin.Web Live Routing]*

See that? The request just came in. The classifier ran locally on `llama3.1:8b`, assigned intent `simple-chat` with 92% confidence, the rule "Simple chat" matched, and it was routed to the **local Ollama model**. Private. Free. Sub-second.

You can even see the routing footer injected into the Copilot reply: "🧭 Harness · rule 'Simple chat' → ollama llama3.1 · 45 tok."

Now watch what happens with a big code task. *[type a complex multi-file refactoring prompt]* — classifier says `code-task`, rule "Complex code tasks" matched, routed to `foundry gpt-5-mini` in Azure. Power when you need it.

---

### [3:00–4:00] — Show the rules editor

*[Show: Admin.Web → Rules page]*

Here's the rules editor. Every rule is a set of conditions and a target model. You can match on keywords, regex, prompt size, intent, streaming mode — whatever you need.

*[Click "Test rule" on an existing rule]*

There's a built-in rule tester. Type a prompt, see which rule it would match and why. No guessing.

*[Show one rule's condition list: IntentEquals, PromptSizeOver, etc.]*

Want all "GitHub Actions" questions to stay local? One rule with an `IntentEquals: github-actions` condition. Done.

---

### [4:00–4:30] — The VS Code extension moment

*[Show: VS Code status bar with last routed model visible]*

If you install the VS Code extension — included in the repo — you get the last routed model right here in the status bar, and a command to open the routing dashboard from inside VS Code. Demo feedback loops don't get tighter than this.

---

### [4:30–5:00] — Close and CTA

*[Show: closing slide with GitHub URL]*

So to recap: one `aspire run`, one BYOK config in VS Code, and every Copilot request is now routed by **your rules** — local for privacy and cost, cloud for power, with full explainability on every single turn.

It's open source, MIT license, built on .NET 10 and .NET Aspire. All the links are in the description.

*[Pause, smile]*

Give it a star if you find it useful — and I'll see you in the next one.

**[github.com/elbruno/ElBruno.CopilotHarness]**

---

## PRESENTER NOTES

- Total runtime target: 4:45–5:00 minutes. Don't rush the demo — real routing is the wow moment.
- If demo fails: fall back to the animated slide deck (`docs/presentation/harness-layers.html`) and narrate the layers. The deck is self-contained and opens in any browser.
- The routing footer in Copilot chat (Layer 3 demo) requires `ResponseAnnotation__Enabled=true` in AppHost — this is the default for local `aspire run`.
- Local Ollama must be running with `llama3.1:8b` pulled: `ollama pull llama3.1:8b`.
- Keep the Admin.Web Live Routing page open and auto-refreshing during the VS Code demo — switching windows while a request is in flight shows the realtime routing magic.
