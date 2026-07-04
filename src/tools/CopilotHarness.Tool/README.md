# copilot-harness

A .NET global tool for setting up and running the **Copilot Harness** agent infrastructure in any repository.

## Installation

```bash
dotnet tool install -g copilot-harness
```

## Commands

### `harness init`

Copies Copilot Harness agent files into the current directory and auto-configures VS Code.

```bash
harness init
```

Creates:
- `.github/agents/harness-general.agent.md`
- `.github/agents/harness-launch.agent.md`
- `.github/agents/harness-github.agent.md`
- `.github/agents/harness-debug.agent.md`
- `.github/agents/harness-db.agent.md`
- `.github/agents/harness-test.agent.md`
- `.github/agents/harness-docs.agent.md`
- `.github/agents/harness-deploy.agent.md`

**Auto-writes `chatLanguageModels.json`** to your VS Code user config folder — no manual file placement needed. phi-4-mini is registered as a BYOK model immediately after `harness init` completes.

**Aspire detection** — if an Aspire AppHost project is found anywhere in the current directory tree, `harness init` surfaces a suggestion for wiring FoundryLocalProxy into your Aspire orchestration.

### `harness start`

Starts the **FoundryLocalProxy** (runs `dotnet run` in `proxies/FoundryLocalProxy/`).

```bash
harness start

# If the proxy is not in a parent directory, specify the path:
harness start --proxy-path /path/to/FoundryLocalProxy
```

The proxy listens on `http://localhost:5101`.

### `harness status`

Health-checks the proxy and displays loaded models and configuration.

```bash
harness status
```

### `harness doctor`

Validates the complete setup and reports any issues.

```bash
harness doctor
```

Example output:
```
[✓] Aspire CLI installed        aspire 1.4.0
[✓] FoundryLocalProxy running   http://localhost:5101/health → 200 OK
[✓] Agent files present         .github/agents/ (8 files)
[✓] VS Code config written      chatLanguageModels.json found in VS Code user dir
```

| Check | How |
|---|---|
| Aspire CLI installed | `aspire --version` |
| FoundryLocalProxy running | GET `http://localhost:5101/health` |
| Agent files present | Looks for `.github/agents/harness-general.agent.md` |
| VS Code config written | Checks for `chatLanguageModels.json` in VS Code user dir |

### `harness update`

Re-extracts the latest agent templates into `.github/agents/`.

```bash
harness update
```

Example output:
```
[Added]      harness-db.agent.md
[Updated]    harness-general.agent.md
[Up to date] harness-launch.agent.md
[Up to date] harness-github.agent.md
[Up to date] harness-debug.agent.md
```

Reports:
- **Added** — new files not previously present
- **Updated** — files that have changed since last init/update
- **Up to date** — files that already match the latest templates

## Quick start

```bash
# 1. Install the tool
dotnet tool install -g copilot-harness

# 2. In your repo root — copies agent files AND auto-writes VS Code config:
harness init

# 3. Start the local proxy (easiest: aspire start from the proxies/ folder):
cd /path/to/ElBruno.CopilotHarness/proxies && aspire start
# Or start just FoundryLocalProxy:
harness start

# 4. Validate the full setup (Aspire CLI, proxy, agent files, VS Code config):
harness doctor

# 5. In VS Code Copilot Chat:
# @harness-general What can you help me with?
```

## Building from source

```bash
cd tools/CopilotHarness.Tool
dotnet build
dotnet pack
dotnet tool install -g --add-source ./nupkg copilot-harness
```
