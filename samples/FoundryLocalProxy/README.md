# FoundryLocalProxy

A minimal ASP.NET Core proxy that lets VS Code Copilot treat a local
[Microsoft Foundry Local](https://github.com/microsoft/Foundry-Local) model as a
BYOK (Bring Your Own Model) provider — no Azure account, no internet after first run,
no per-token cost, no CLI required.

Uses the official **Foundry Local C# SDK** (`Microsoft.AI.Foundry.Local`) so the proxy
manages the daemon, model downloads, hardware detection, and REST server itself.

**Port:** `http://localhost:5101`
_(OllamaProxy uses 5099, FoundryProxy uses 5100 — all three can run together)_

---

## How model download works

The proxy uses the SDK, not the CLI. At startup it runs this sequence automatically:

| Step | What happens | First run | Subsequent runs |
|------|-------------|-----------|-----------------|
| 1 | Start Foundry Local daemon | SDK launches it | SDK re-attaches |
| 2 | Download GPU/NPU execution providers | Downloads ~MB | Already cached — instant |
| 3 | `model.DownloadAsync("phi-4-mini")` | Downloads ~2.5 GB | **Skipped — already cached** |
| 4 | `model.LoadAsync()` | Loads into RAM | Loads into RAM |
| 5 | `mgr.StartWebServiceAsync()` | Starts internal REST :55588 | Starts internal REST :55588 |
| 6 | Proxy accepts VS Code BYOK requests on :5101 | Ready | Ready |

`DownloadAsync()` is **idempotent** — it checks the local cache first and skips the
download if the model is already there (`%USERPROFILE%\.foundry\cache` on Windows).

---

## Architecture

```
VS Code Copilot
      │  POST /v1/chat/completions  { "model": "phi-4-mini", ... }
      ▼
FoundryLocalProxy  :5101          ← stable BYOK endpoint VS Code knows
      │  model pass-through / utility alias rewrite
      │  [copilot ask] logging
      ▼
Foundry Local SDK internal REST  :55588    ← managed entirely by SDK
      │
      ▼
phi-4-mini  (downloaded + cached + hardware-optimised by SDK)
```

---

## Quick start

```bash
cd samples/FoundryLocalProxy
dotnet run
```

No `foundry model run` needed — the SDK handles everything.
On first run watch the console; a ~2.5 GB download happens once then is cached.

Verify it is alive:
```bash
curl http://localhost:5101/health
```

---

## Register with VS Code

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

A ready-to-paste copy of this snippet is in `vscode-settings.json` next to this README.

---

## Configuration

All settings live in `appsettings.json` and can be overridden with environment variables
using the standard ASP.NET Core double-underscore separator:

| Setting | Default | Env var | Description |
|---------|---------|---------|-------------|
| `FoundryLocal:DefaultModel` | `phi-4-mini` | `FoundryLocal__DefaultModel` | Model alias to download + load at startup |
| `FoundryLocal:AdditionalModels` | `[]` | `FoundryLocal__AdditionalModels__0=...` | Extra model aliases to load alongside DefaultModel |
| `FoundryLocal:InternalPort` | `55588` | `FoundryLocal__InternalPort` | Port for SDK internal REST server |
| `FoundryLocal:UtilityModelId` | `copilot-utility-small` | `FoundryLocal__UtilityModelId` | Synthetic alias for VS Code utility model slot |
| `FoundryLocal:DownloadExecutionProviders` | `true` | `FoundryLocal__DownloadExecutionProviders` | Auto-download GPU/NPU ONNX Runtime extensions |

### Loading multiple models

```json
{
  "FoundryLocal": {
    "DefaultModel": "phi-4-mini",
    "AdditionalModels": [ "phi-3.5-mini", "llama-3.2-3b" ]
  }
}
```

All three models are downloaded (once), loaded, and advertised from `GET /v1/models`.
Switch between them in VS Code without restarting the proxy.

### Recommended model aliases

| Alias | Parameters | Best for | Download size |
|-------|-----------|----------|---------------|
| `phi-4-mini` | 3.8B | Coding, reasoning, general **(default)** | ~2.5 GB |
| `phi-3.5-mini` | 3.8B | General chat | ~2.3 GB |
| `llama-3.2-3b` | 3B | Ultra-fast, very lightweight | ~1.8 GB |
| `mistral-7b-instruct` | 7B | Strong instruction following | ~4.1 GB |
| `qwen2.5-0.5b` | 0.5B | Ultra-light, fastest startup | ~0.4 GB |

Run `foundry model list` (if CLI is installed) to see the full catalog for your hardware.

---

## Windows: WinML for maximum hardware acceleration

The default package (`Microsoft.AI.Foundry.Local`) supports Windows, macOS, and Linux.
For maximum NPU/GPU performance on Windows, swap to the WinML variant in the csproj:

```xml
<!-- Replace the existing PackageReference with: -->
<PackageReference Include="Microsoft.AI.Foundry.Local.WinML" Version="*" />
```

And update the TargetFramework:
```xml
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<Platforms>ARM64;x64</Platforms>
```

The API surface is identical — only the hardware backends change.

---

## Using with Harness agents

This proxy is the local model backend for the **Harness multi-agent pattern**:

- `@harness-general` — orchestrator, uses cloud Copilot model
- `@harness-launch` — app lifecycle specialist, **uses phi-4-mini via this proxy**
- `@harness-github` — GitHub/PR/Actions specialist, **uses phi-4-mini via this proxy**
- `@harness-debug` — error analysis specialist, **uses phi-4-mini via this proxy**

See [`docs/Agents_Architecture.md`](../../docs/Agents_Architecture.md) for the full setup.

---

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | Health + loaded models summary |
| `GET` | `/health` | Same as `/` |
| `GET` | `/v1/models` | OpenAI-compatible model list |
| `POST` | `/v1/chat/completions` | Chat proxy (streaming + non-streaming) |
