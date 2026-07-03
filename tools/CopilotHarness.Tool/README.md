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
- `copilot-harness-settings.json` — merge this into your VS Code `settings.json`

### `harness start`

Starts the **FoundryLocalProxy** (runs `dotnet run` in `samples/FoundryLocalProxy/`).

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

# 3. Merge copilot-harness-settings.json into your VS Code settings.json

# 4. Start the local proxy (from the ElBruno.CopilotHarness repo):
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
