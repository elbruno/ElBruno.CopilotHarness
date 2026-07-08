using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Router.Api.Extension;

/// <summary>
/// Self-service endpoints that help a user wire the harness into GitHub Copilot's
/// "Bring Your Own Key" (BYOK) custom endpoint in VS Code without hand-writing JSON.
/// <list type="bullet">
/// <item><c>GET /v1/vscode-config</c> returns a ready-to-paste <c>chatLanguageModels.json</c> document.</item>
/// <item><c>GET /connect</c> renders a friendly HTML page with copy / download helpers.</item>
/// </list>
/// The chat completions URL is derived from the inbound request so it is always correct for
/// whatever host/port the user reached the router on.
/// </summary>
public static class VsCodeConnectEndpoints
{
    private const string DefaultModelId = "elbruno.copilotharness";
    private const string DefaultConfigName = "SmartRouter";
    private const string ApiKeyInputReference = "${input:chat.lm.secret.copilotharness}";

    public static IEndpointRouteBuilder MapVsCodeConnectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/vscode-config", (HttpContext context, string? modelId, string? name) =>
        {
            var config = BuildConfig(ResolveChatUrl(context), modelId, name);
            return Results.Text(config.ToJsonString(SerializerOptions), "application/json");
        });

        endpoints.MapGet("/connect", (HttpContext context, string? modelId, string? name) =>
        {
            var chatUrl = ResolveChatUrl(context);
            var json = BuildConfig(chatUrl, modelId, name).ToJsonString(SerializerOptions);
            return Results.Text(BuildHtml(chatUrl, json), "text/html");
        });

        return endpoints;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>Builds the <c>{scheme}://{host}/v1/chat/completions</c> URL from the inbound request.</summary>
    internal static string ResolveChatUrl(HttpContext context)
    {
        var request = context.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}".TrimEnd('/');
        return $"{baseUrl}/v1/chat/completions";
    }

    /// <summary>Builds the <c>chatLanguageModels.json</c> document (a single-element array) for the given chat URL.</summary>
    internal static JsonArray BuildConfig(string chatUrl, string? modelId = null, string? name = null)
    {
        var id = string.IsNullOrWhiteSpace(modelId) ? DefaultModelId : modelId.Trim();
        var displayName = string.IsNullOrWhiteSpace(name) ? DefaultConfigName : name.Trim();

        return new JsonArray
        {
            new JsonObject
            {
                ["name"] = displayName,
                ["vendor"] = "customendpoint",
                ["apiKey"] = ApiKeyInputReference,
                ["apiType"] = "chat-completions",
                ["models"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = id,
                        ["name"] = id,
                        ["url"] = chatUrl,
                        ["toolCalling"] = true,
                        ["vision"] = true,
                        ["maxInputTokens"] = 128000,
                        ["maxOutputTokens"] = 16000
                    }
                }
            }
        };
    }

    private static string BuildHtml(string chatUrl, string json)
    {
        var encodedUrl = WebUtility.HtmlEncode(chatUrl);
        var encodedJson = WebUtility.HtmlEncode(json);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Connect to VS Code · Copilot Harness</title>
  <style>
    :root { color-scheme: light dark; }
    body { font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; max-width: 820px; margin: 2rem auto; padding: 0 1rem; line-height: 1.5; }
    h1 { font-size: 1.5rem; }
    code { background: rgba(127,127,127,.18); padding: .1rem .35rem; border-radius: 4px; }
    ol { padding-left: 1.25rem; }
    pre { background: rgba(127,127,127,.12); border: 1px solid rgba(127,127,127,.3); border-radius: 8px; padding: 1rem; overflow: auto; }
    .row { display: flex; gap: .5rem; flex-wrap: wrap; margin: .75rem 0; }
    button { font: inherit; padding: .5rem .9rem; border-radius: 6px; border: 1px solid rgba(127,127,127,.4); background: #2563eb; color: #fff; cursor: pointer; }
    button.secondary { background: transparent; color: inherit; }
    .muted { opacity: .8; font-size: .92rem; }
    .ok { color: #16a34a; font-weight: 600; }
  </style>
</head>
<body>
  <h1>Connect this harness to GitHub Copilot (VS Code BYOK)</h1>
  <p class="muted">Chat endpoint: <code>{{encodedUrl}}</code></p>
  <ol>
    <li>In VS Code, open <strong>Copilot Chat</strong> &rarr; the model picker &rarr; <strong>Manage Models</strong> (or run <em>Chat: Manage Language Models</em>).</li>
    <li>Choose <strong>Add Models</strong> &rarr; <strong>Custom Endpoint</strong>. When the JSON editor opens, replace its contents with the config below.</li>
    <li>Save, pick the model in Copilot Chat, and send a prompt. VS Code will ask for an API key on first use &mdash; any non-empty value works unless the router enforces an admin key.</li>
  </ol>
  <div class="row">
    <button id="copy" type="button">Copy config</button>
    <button id="download" type="button" class="secondary">Download chatLanguageModels.json</button>
    <span id="status" class="ok" hidden>Copied!</span>
  </div>
  <pre id="config">{{encodedJson}}</pre>
  <p class="muted">The model <code>id</code>/<code>name</code> is just a label &mdash; the router selects the real model from your routing rules.</p>
  <script>
    const json = document.getElementById('config').textContent;
    const status = document.getElementById('status');
    document.getElementById('copy').addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(json); }
      catch { const r = document.createRange(); r.selectNode(document.getElementById('config')); getSelection().removeAllRanges(); getSelection().addRange(r); document.execCommand('copy'); }
      status.hidden = false; setTimeout(() => status.hidden = true, 1500);
    });
    document.getElementById('download').addEventListener('click', () => {
      const blob = new Blob([json], { type: 'application/json' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = 'chatLanguageModels.json';
      a.click();
      URL.revokeObjectURL(a.href);
    });
  </script>
</body>
</html>
""";
    }
}
