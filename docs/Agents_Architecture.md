# Harness Agents Architecture

Complete implementation plan and reference for the multi-agent, mixed-model Copilot
setup in this repository.

---

## Core concept

The same routing insight that drives the CopilotHarness router applies directly
to Copilot Agents: **not every task needs a cloud model.**

A launch-app or GitHub task agent running on **phi-4-mini locally** is fast, free,
offline, and accurate enough for structured/procedural work. The orchestrator uses
the full Copilot cloud model only where reasoning depth actually matters.

```
User  →  @harness-general  (cloud Copilot model — reasoning, architecture, routing)
                │
                ├── "start the app"      →  @harness-launch  (phi-4-mini, local)
                ├── "open a PR"          →  @harness-github  (phi-4-mini, local)
                └── "debug this error"   →  @harness-debug   (phi-4-mini, local)
```

**Cost model:**

| Request type | Cloud-only | This pattern |
|---|---|---|
| "run the API" | ~500 cloud tokens | 0 cost (local) |
| "create an issue" | ~800 cloud tokens | 0 cost (local) |
| "analyse this error" | ~1 200 cloud tokens | 0 cost (local) |
| "review this architecture" | ~3 000 cloud tokens | ~3 000 cloud tokens (general) |
| **Typical 10-task session** | **~8 000 tokens** | **~1 500 cloud + 0 local** |

---

## Agent files

All agents live in `.github/agents/` and are picked up automatically by VS Code Copilot.

| File | `@name` | `model` field | Selectable | Purpose |
|------|---------|--------------|-----------|---------|
| `harness-general.agent.md` | `@harness-general` | _(Copilot default)_ | ✅ Yes | Orchestrator — routes, synthesises, answers architecture Qs |
| `harness-launch.agent.md` | `@harness-launch` | `phi-4-mini` | ❌ Sub-agent | App lifecycle: start, stop, port conflicts |
| `harness-github.agent.md` | `@harness-github` | `phi-4-mini` | ❌ Sub-agent | GitHub: issues, PRs, Actions, releases |
| `harness-debug.agent.md` | `@harness-debug` | `phi-4-mini` | ❌ Sub-agent | Error analysis, stack traces, test failures |

The `model: phi-4-mini` field in sub-agent frontmatter tells VS Code to use that BYOK
model for those agents. `phi-4-mini` must be registered in VS Code settings (see below).

---

## FoundryLocalProxy — the local model backend

The sub-agents use **phi-4-mini** via **FoundryLocalProxy** (`samples/FoundryLocalProxy`).

### How it works

```
@harness-launch (VS Code)
      │  model: phi-4-mini  (registered BYOK endpoint)
      ▼
FoundryLocalProxy  :5101          ← stable, known port for VS Code
      │
      ▼
Foundry Local SDK internal REST  :55588    ← SDK-managed
      │
      ▼
phi-4-mini  (downloaded once, cached, hardware-optimised by SDK)
```

### What the SDK does

The proxy uses `Microsoft.AI.Foundry.Local` (official NuGet package). At startup:

1. **Starts the Foundry Local daemon** — no separate `foundry service start` needed
2. **Downloads GPU/NPU execution providers** — one-time, cached; picks best for your hardware
3. **`model.DownloadAsync()`** — downloads phi-4-mini (~2.5 GB) if not in local cache; **skips entirely on subsequent runs**
4. **`model.LoadAsync()`** — loads weights into memory
5. **`mgr.StartWebServiceAsync()`** — starts internal OpenAI-compatible REST on :55588
6. **Proxy opens on :5101** — ready for VS Code BYOK requests

**No CLI required.** No `winget install Microsoft.FoundryLocal` needed.
The SDK ships everything it needs as NuGet dependencies.

### Starting the proxy

```bash
cd samples/FoundryLocalProxy
dotnet run
```

First run output (abbreviated):
```
  [EP] CUDA Execution Provider             100%
  [EP] QNN Execution Provider              100%
  [download] phi-4-mini                    100%
  Model 'phi-4-mini' ready (id: phi-4-mini-cuda-int4-awq-block-128-acc-level-4).
  Internal REST server ready.
  Proxy ready. Loaded models: phi-4-mini-cuda-...  Fallback: phi-4-mini-cuda-...
  Now listening on: http://localhost:5101
```

Subsequent runs:
```
  [download] phi-4-mini                      0% (skipped — cached)
  Model 'phi-4-mini' ready.
  Now listening on: http://localhost:5101
```

---

## VS Code setup

### Step 1 — Register phi-4-mini as a BYOK model

Open **Command Palette → Preferences: Open User Settings (JSON)** and add:

```json
{
  "github.copilot.chat.customModels": [
    {
      "id": "phi-4-mini",
      "name": "Phi-4 Mini (Foundry Local)",
      "url": "http://localhost:5101/v1/chat/completions",
      "modelListUrl": "http://localhost:5101/v1/models",
      "authType": "none"
    },
    {
      "id": "copilot-utility-small",
      "name": "Phi-4 Mini Utility (Foundry Local)",
      "url": "http://localhost:5101/v1/chat/completions",
      "modelListUrl": "http://localhost:5101/v1/models",
      "authType": "none"
    }
  ],
  "chat.utilitySmallModel": "copilot-utility-small"
}
```

A ready-to-paste version of this snippet is at `samples/FoundryLocalProxy/vscode-settings.json`.

### Step 2 — Start the proxy

```bash
cd samples/FoundryLocalProxy && dotnet run
```

### Step 3 — Use the agents

In VS Code Copilot Chat:
```
@harness-general start the web API
@harness-general open a PR for the feature branch
@harness-general why is AuthController throwing a NullReferenceException?
@harness-general review the architecture of the Router service
```

The general agent routes the first three to local sub-agents; the fourth it handles itself.

---

## Adding more models

Edit `samples/FoundryLocalProxy/appsettings.json`:

```json
{
  "FoundryLocal": {
    "DefaultModel": "phi-4-mini",
    "AdditionalModels": [ "phi-3.5-mini", "llama-3.2-3b" ]
  }
}
```

Restart the proxy — all models are downloaded (if needed) and loaded.
Add corresponding entries to `github.copilot.chat.customModels` for each alias you want
to be able to select in VS Code.

---

## Proxy alternatives

All three proxies can run simultaneously:

| Proxy | Port | NuGet/backend | Auth | Use case |
|-------|------|--------------|------|---------|
| `OllamaProxy` | 5099 | None (raw HTTP to Ollama) | None | llama3.1:8b, qwen2.5 via Ollama |
| `FoundryProxy` | 5100 | None (raw HTTP to Azure) | API key | Azure Foundry / Azure OpenAI |
| **`FoundryLocalProxy`** | **5101** | `Microsoft.AI.Foundry.Local` | **None** | **phi-4-mini local — recommended for agents** |

To use OllamaProxy as the sub-agent backend instead, change the model `id` / `url`
in VS Code settings to point to `:5099` and update `model: llama3.1:8b` in the
sub-agent frontmatter.

---

## Distribution: the `harness` dotnet tool

A .NET global tool (`tools/CopilotHarness.Tool/`) lets any team adopt this pattern
in their own repo with a single command.

### Installation

```bash
# From NuGet (once published):
dotnet tool install -g copilot-harness

# From source (this repo):
cd tools/CopilotHarness.Tool
dotnet pack
dotnet tool install -g --add-source ./nupkg copilot-harness
```

### Commands

```bash
# Copy agent files + VS Code settings into the current repo
harness init

# Start FoundryLocalProxy (downloads model on first run)
harness start

# Check if the proxy is healthy
harness status
```

`harness init` creates:
```
.github/
└── agents/
    ├── harness-general.agent.md
    ├── harness-launch.agent.md
    ├── harness-github.agent.md
    └── harness-debug.agent.md
copilot-harness-settings.json   ← paste into VS Code settings.json
```

### Alternative: GitHub Template

For greenfield projects, this repository can be used as a GitHub template.
Click **"Use this template"** to clone with all agents pre-configured.
Not suitable for adding to an existing repo — use `harness init` for that.

---

## Extending the pattern

Add new specialized sub-agents by:

1. Creating `.github/agents/harness-{domain}.agent.md` with `model: phi-4-mini` in frontmatter
2. Adding a routing row to the table in `harness-general.agent.md`
3. No proxy changes needed — the new agent inherits the FoundryLocalProxy backend

Candidate domains:
- `harness-db` — SQL queries, migration generation, schema inspection
- `harness-test` — test scaffold generation, coverage gaps, mutation testing
- `harness-deploy` — IaC, Docker, deployment scripts, CI pipeline generation
- `harness-docs` — docstring generation, changelog drafting, README updates
