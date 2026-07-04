# Troubleshooting: FoundryLocalProxy & VS Code Copilot

---

## "No utility model is configured for 'copilot-utility-small' while the selected main model is BYOK"

### When it appears
Agent mode in VS Code Copilot Chat (the **Agent** tab, not **Ask**) when using
a BYOK endpoint such as FoundryLocalProxy.

### Root cause
VS Code uses two background utility models for lightweight tasks:
- `chat.utilityModel` — title generation, summaries, settings search, Git review
- `chat.utilitySmallModel` — commit messages, rename hints, branch names, intent detection

When you sign in with a GitHub Copilot subscription these default to Copilots hosted
models. When you use BYOK **without** signing into GitHub, those hosted models are
unreachable and VS Code shows the error above.

> Official docs: https://code.visualstudio.com/docs/agent-customization/language-models#_configure-models-for-other-features
> "If you use BYOK models without signing into a GitHub account, the built-in utility
> models are not available. Set chat.utilityModel and chat.utilitySmallModel to a BYOK model."

### Fix

**Step 1 — Register phi-4-mini via the Language Models editor:**
Open VS Code → Copilot Chat model picker → **Manage Models** → **Add Models** → **Custom Endpoint**  
_(or Command Palette: `Chat: Manage Language Models`)_

This opens/creates `chatLanguageModels.json`. Add or merge:

```json
[
  {
    "name": "Phi-4 Mini (Foundry Local)",
    "vendor": "customendpoint",
    "apiType": "chat-completions",
    "models": [
      {
        "id": "phi-4-mini",
        "name": "Phi-4 Mini (Foundry Local)",
        "url": "http://localhost:5101/v1/chat/completions",
        "toolCalling": true,
        "maxInputTokens": 131072,
        "maxOutputTokens": 4096
      }
    ]
  }
]
```

**Step 2 — Set the utility model** in `settings.json` (Ctrl+Shift+P → Preferences: Open User Settings JSON):

```json
{
  "chat.utilityModel": "phi-4-mini",
  "chat.utilitySmallModel": "phi-4-mini"
}
```

Then reload VS Code (Ctrl+Shift+P → Reload Window).

### Why phi-4-mini, not copilot-utility-small?

Using `copilot-utility-small` as both the BYOK model ID AND the value of
`chat.utilitySmallModel` creates a circular lookup that VS Code cannot resolve
(it tries to find its own internal copilot-utility-small model, not your BYOK one).
Use the actual BYOK model ID (`phi-4-mini`) for both utility settings.

---

## "Sorry, no response was returned"

### Cause A - Proxy not running
```
cd src/proxies/FoundryLocalProxy && dotnet run
```
First run downloads phi-4-mini (~2.5 GB). Subsequent runs start in seconds (cached).

### Cause B - Non-standard SDK streaming format
The Foundry Local SDK emits non-standard fields in SSE chunks. Older proxy builds
forwarded these raw; VS Code rejects them silently. Fields that caused failures:

| Field | Problem |
|---|---|
| model: "Phi-4-mini-instruct-cuda-gpu:5" | Doesnt match requested phi-4-mini |
| tool_calls:[] in delta | VS Code treats any tool_calls entry as a tool event |
| IsDelta, Successful, HttpStatusCode | Non-standard fields |
| Duplicate data:[DONE] | VS Code errors after seeing data after the first [DONE] |

Fix: pull latest code and restart proxy.

### Cause C - Model ID not in /v1/models
VS Code validates the configured model id against modelListUrl before sending requests.
Early builds returned only canonical SDK ids, not the configured alias phi-4-mini.
Fix: already fixed in current proxy. Restart after git pull.

---

## Ask mode works, Agent mode fails

chatLanguageModels.json covers Ask mode and vscode.lm API (agent .md files).
Agent mode additionally requires `chatLanguageModels.json` (for model registration) + utility model
settings in `settings.json`. See the fix above.

| File | Covers |
|---|---|
| chatLanguageModels.json | Ask mode, @harness-* sub-agents |
| settings.json customModels + utility | Agent mode |

---

## Proxy starts but model download fails

SDK downloads to %USERPROFILE%\.foundry\cache. Requires ~2.5 GB free and internet.
If download stalls:
```
rd /s /q %USERPROFILE%\.foundry\cache
cd src/proxies/FoundryLocalProxy && dotnet run
```

---

## Port 5101 already in use

```powershell
netstat -ano | Select-String ":5101 "
# Note PID in last column, then:
Stop-Process -Id <PID> -Force
```

---

## Known limitations (VS Code Insiders, July 2026)

- BYOK + Agent mode: both utility model settings must be explicitly configured.
  There is no auto-fallback to the main model. Issue: github.com/microsoft/vscode/issues/324007
- phi-4-mini supports single-turn tool calling; complex multi-turn agent reasoning
  works better with a cloud model.

