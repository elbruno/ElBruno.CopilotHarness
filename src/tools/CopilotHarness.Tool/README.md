# copilot-harness

A .NET global tool for setting up and running the **Copilot Harness** agent infrastructure in any repository.

## Installation

```bash
dotnet tool install -g copilot-harness
```

## Commands

### `harness init`

Copies Copilot Harness agent files and VS Code settings into the current directory.

```bash
harness init
```

Creates:
- `.github/agents/harness-general.agent.md`
- `.github/agents/harness-launch.agent.md`
- `.github/agents/harness-github.agent.md`
- `.github/agents/harness-debug.agent.md`
- `copilot-harness-settings.json` — rename to `chatLanguageModels.json` and place in your VS Code user config folder

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

## Quick start

```bash
# 1. Install the tool
dotnet tool install -g copilot-harness

# 2. In your repo root:
harness init

# 3. Configure VS Code language models (choose one):
#    a) Rename copilot-harness-settings.json to chatLanguageModels.json
#       and place it in your VS Code user config folder, OR
#    b) Use the Language Models editor:
#       model picker → Manage Models → Add Models → Custom Endpoint

# 4. Start the local proxy (easiest: aspire start from the proxies/ folder):
cd /path/to/ElBruno.CopilotHarness/proxies && aspire start
# Or start just FoundryLocalProxy:
harness start

# 5. Check health
harness status

# 6. In VS Code Copilot Chat:
# @harness-general What can you help me with?
```

## Building from source

```bash
cd tools/CopilotHarness.Tool
dotnet build
dotnet pack
dotnet tool install -g --add-source ./nupkg copilot-harness
```
