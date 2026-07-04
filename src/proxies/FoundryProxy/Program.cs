// =============================================================================
//  FoundryProxy — minimal OpenAI-compatible proxy for Microsoft Foundry /
//                 Azure OpenAI.
//
//  PURPOSE (teaching sample)
//  -------------------------
//  This is intentionally the SMALLEST possible proxy that lets VS Code Copilot
//  treat an Microsoft Foundry (Azure OpenAI) deployment as a BYOK (Bring Your
//  Own Key / Bring Your Own Model) provider.  Every line is commented so it
//  can be read on stage.
//
//  WHY A WEB APP AND NOT A CONSOLE APP?
//  -------------------------------------
//  BYOK in VS Code requires an HTTP endpoint.  VS Code's model-provider
//  registration ("chatLanguageModels.json") points to a URL such as
//  http://localhost:5100/v1/chat/completions.  A plain console app has no
//  TCP listener so the editor can't reach it.  ASP.NET Core Minimal API gives
//  us that listener with almost zero ceremony — the startup code is ~10 lines.
//
//  WHY NO AZURE SDK?
//  -----------------
//  The Azure OpenAI REST API is a straightforward HTTP API.  We speak to it
//  directly using HttpClient — the same approach used in the main project's
//  ChatCompletionsProvider.cs.  This keeps the sample dependency-free and
//  makes the auth header (api-key) and URL structure fully visible on stage.
//
//  SECRETS — THE WHOLE POINT
//  --------------------------
//  Azure credentials (Endpoint, ApiKey, Deployment) are loaded from .NET User
//  Secrets, NOT from appsettings.json.  User Secrets are stored per-user on
//  disk, outside the repo, and are NEVER committed to source control.
//  This is the security teaching point: the VS Code client never sees the key;
//  it only knows the local proxy URL (http://localhost:5100).
//
//  FIXED PORT: http://localhost:5100
//  All README instructions and the VS Code snippet use this port.
//  (OllamaProxy uses 5099 — keeping them distinct so both can run together.)
// =============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using FoundryProxy;
using Proxies.ServiceDefaults;

// ---------------------------------------------------------------------------
// 1. BUILDER — wire up services before building the app
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// AddProxiesServiceDefaults wires OpenTelemetry (traces + metrics + logs) and
// sends data to Aspire via OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set.
builder.AddProxiesServiceDefaults();

// ---------------------------------------------------------------------------
// USER SECRETS — explicitly add them so they load regardless of ASPNETCORE_ENVIRONMENT.
//
// WHY THIS EXPLICIT CALL?
//   WebApplication.CreateBuilder() automatically loads user secrets when
//   ASPNETCORE_ENVIRONMENT == "Development" (the default for `dotnet run`).
//   In a demo it is easy to accidentally have a different environment set, or
//   to run from a script that overrides it.  Calling AddUserSecrets explicitly
//   guarantees the secrets are always loaded — Development or not.
//   The UserSecretsId in FoundryProxy.csproj tells the runtime which
//   secrets.json file to look for.
// ---------------------------------------------------------------------------
builder.Configuration.AddUserSecrets(typeof(Program).Assembly);

// ---------------------------------------------------------------------------
// Read Foundry settings from configuration (appsettings.json + user secrets).
//
// PRECEDENCE (highest wins):
//   1. Environment variables  (Foundry__Endpoint=https://...)
//   2. User Secrets           (set with: dotnet user-secrets set "Foundry:Endpoint" "...")
//   3. appsettings.json       (contains empty placeholders — safe to commit)
//
// EXPECTED INPUTS → OUTPUTS for the endpoint trimming logic (GetAzureResourceBase):
//   "https://myresource.openai.azure.com"           → "https://myresource.openai.azure.com/"
//   "https://myresource.openai.azure.com/"          → "https://myresource.openai.azure.com/"
//   "https://myresource.openai.azure.com/openai"    → "https://myresource.openai.azure.com/"
//   "https://myresource.openai.azure.com/openai/v1" → "https://myresource.openai.azure.com/"
//
// The deployment path is then appended:
//   {resourceBase}openai/deployments/{deployment}/chat/completions?api-version={apiVersion}
// ---------------------------------------------------------------------------
var foundryEndpoint      = builder.Configuration["Foundry:Endpoint"]       ?? string.Empty;
var foundryApiKey        = builder.Configuration["Foundry:ApiKey"]         ?? string.Empty;
var foundryDeployment    = builder.Configuration["Foundry:Deployment"]     ?? string.Empty;
var foundryApiVersion    = builder.Configuration["Foundry:ApiVersion"]     ?? "2024-10-21";

// Newer Foundry models (gpt-5.x, gpt-chat-latest, o-series) only accept the
// default temperature/top_p of 1. When true (the default) we strip any other
// value so Copilot's temperature: 0.1 doesn't trigger a 400. Set to false in
// appsettings.json if you target an older model that honors custom sampling.
var stripUnsupportedSamplingParams =
    builder.Configuration.GetValue<bool?>("Foundry:StripUnsupportedSamplingParams") ?? true;

// Optional list of Azure deployment names this proxy is allowed to route to.
//
//  THE "PASS-THROUGH ALLOWLIST" (parity with the Ollama sample):
//    When VS Code requests one of these model ids, the proxy uses that id AS
//    the deployment name in the Azure URL path — so selecting different models
//    in VS Code actually hits different Foundry deployments from one endpoint.
//
//  WHY CONFIG AND NOT AUTO-DISCOVERY?
//    The Ollama sample discovers installed models via Ollama's GET /api/tags.
//    Azure OpenAI has no equivalent that works with just a data-plane api-key
//    (listing deployments is a control-plane / ARM operation), so you declare
//    your deployment names here instead — in appsettings.json:
//      "Foundry": { "Deployments": [ "gpt-5.5", "gpt-chat-latest" ] }
//    or via env: Foundry__Deployments__0=gpt-5.5  Foundry__Deployments__1=...
//
//    Leave it EMPTY to allow ANY requested model id to be used as a deployment
//    name (the utility alias still falls back to the default deployment).
var configuredDeployments = builder.Configuration
    .GetSection("Foundry:Deployments")
    .Get<string[]>() ?? Array.Empty<string>();
var deploymentAllowlist = new HashSet<string>(configuredDeployments, StringComparer.OrdinalIgnoreCase);

// WHY A UTILITY MODEL ID?
//   GitHub Copilot's AGENT surface (the "Describe what to build" input in
//   VS Code) uses TWO separate model "slots" at the same time:
//
//     1. MAIN model   — the model the user selected in the model picker
//                       (e.g. gpt-4o-mini).  Handles the user's chat turns.
//     2. UTILITY model — a lightweight background model VS Code uses for
//                        small, fast tasks: generating chat titles, producing
//                        commit-message suggestions, rename hints, etc.
//
//   When the main model is a BYOK custom endpoint, VS Code loses access to
//   its built-in GitHub-hosted utility models (you're offline / private).
//   It then looks for a BYOK-registered model to fill the utility slot.
//   If none is registered the agent surface shows:
//
//     "No utility model is configured for 'copilot-utility-small'
//      while the selected main model is BYOK."
//
//   FIX (this proxy's approach):
//     • GET  /v1/models  — advertises BOTH the deployment name AND this
//       utility ID so VS Code discovers a valid utility candidate.
//     • POST /v1/chat/completions — the requested model id is used AS the
//       Azure deployment name in the URL path, so selecting different models
//       in VS Code hits different deployments (pass-through).  If the request
//       arrives with model = utilityModelId (e.g. "copilot-utility-small"),
//       or an id not in the configured allowlist, we LOG a rewrite and route
//       it to the DEFAULT deployment instead (see STEP 2 below).
//
//   NOTE: the default Foundry deployment serves the utility slot in this
//   simple sample.  List your real deployments in Foundry:Deployments so the
//   main slot can pass through to each of them.
var utilityModelId       = builder.Configuration["Foundry:UtilityModelId"] ?? "copilot-utility-small";

// ---------------------------------------------------------------------------
// Compute whether Foundry is fully configured (used by /health and the
// fail-fast check in the chat endpoint).
// ---------------------------------------------------------------------------
var isConfigured = !string.IsNullOrWhiteSpace(foundryEndpoint)
                && !string.IsNullOrWhiteSpace(foundryApiKey)
                && !string.IsNullOrWhiteSpace(foundryDeployment);

// ---------------------------------------------------------------------------
// Build the Azure OpenAI target URL from the endpoint secret.
//
// Azure OpenAI REST path:
//   {resourceBase}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}
//
// GetAzureResourceBase strips any trailing /openai or /openai/v1 suffix from
// the endpoint so we never produce a doubled path such as:
//   /openai/v1/openai/deployments/...
// This replicates the logic from FoundryOptions.GetAzureResourceBase in the
// main project (src/ElBruno.CopilotHarness.Router.Api/FoundryOptions.cs).
// The function is re-implemented inline so this sample stays standalone.
// ---------------------------------------------------------------------------
static string GetAzureResourceBase(string endpoint)
{
    var trimmed = (endpoint ?? string.Empty).Trim().TrimEnd('/');

    if (trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        trimmed = trimmed[..^"/openai/v1".Length];
    else if (trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        trimmed = trimmed[..^"/openai".Length];

    return $"{trimmed}/"; // always end with a single slash
}

// The resolved host (resourceBase without the trailing slash) is shown in
// /health so operators can verify routing — but the api-key is NEVER exposed.
var resourceBase = isConfigured ? GetAzureResourceBase(foundryEndpoint) : string.Empty;
var targetHost   = isConfigured ? new Uri(resourceBase).Host : "(not configured)";

// Build the Azure chat-completions URL for a SPECIFIC deployment.
//   {resourceBase}openai/deployments/{deployment}/chat/completions?api-version=...
// The deployment name goes in the URL PATH (that is how Azure selects the
// model), so we compute this PER REQUEST from the resolved deployment — this
// is exactly what lets one proxy serve many deployments.
string BuildRequestUri(string deployment) =>
    $"{resourceBase}openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(foundryApiVersion)}";

// Register a named HttpClient for forwarding requests to Azure OpenAI.
//
// WHY A LONG TIMEOUT?
//   HttpClient's default timeout is 100 seconds.  LLM responses — especially
//   non-streaming ones waiting for a full completion — regularly exceed this
//   for large prompts.  Set it to something generous (5 min here) so the
//   proxy doesn't kill the connection mid-generation.
//   Streaming responses start flowing quickly, but the *connection* still
//   needs to stay open until the model finishes, so the same long timeout
//   applies.
builder.Services.AddHttpClient("foundry", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5); // LLM responses can be SLOW — don't timeout early
});

// ---------------------------------------------------------------------------
// 2. BUILD THE APP — configure fixed port (5100) via Kestrel
// ---------------------------------------------------------------------------
var app = builder.Build();

// Emit a clear, presenter-friendly startup message so anyone watching the
// terminal knows immediately whether the secrets are loaded.
if (!isConfigured)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║  FoundryProxy — AZURE CREDENTIALS NOT CONFIGURED                ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
    Console.WriteLine("║  Run these commands to set your secrets (one-time setup):        ║");
    Console.WriteLine("║                                                                  ║");
    Console.WriteLine("║  cd samples\\FoundryProxy                                        ║");
    Console.WriteLine("║  dotnet user-secrets set \"Foundry:Endpoint\"                     ║");
    Console.WriteLine("║      \"https://<your-resource>.openai.azure.com\"                 ║");
    Console.WriteLine("║  dotnet user-secrets set \"Foundry:ApiKey\" \"<your-key>\"          ║");
    Console.WriteLine("║  dotnet user-secrets set \"Foundry:Deployment\" \"gpt-4o-mini\"    ║");
    Console.WriteLine("║                                                                  ║");
    Console.WriteLine("║  The proxy will start — /health returns 'not configured'.        ║");
    Console.WriteLine("║  Chat requests will return 503 until secrets are set.            ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine($"  FoundryProxy ready — deployment: {foundryDeployment}  host: {targetHost}");
    Console.WriteLine();
    Console.ResetColor();
}

// ---------------------------------------------------------------------------
// 3. ENDPOINTS
// ---------------------------------------------------------------------------

// --- 3a. Health / info endpoint -------------------------------------------
// A quick "is it alive?" check.  Open http://localhost:5100/ in a browser
// or run: curl http://localhost:5100/health
//
// NOTE: the api-key is intentionally NOT included — only the target host is
// shown so operators can verify routing without exposing the credential.
app.MapGet("/", () => new
{
    status       = "ok",
    proxy        = "FoundryProxy",
    configured   = isConfigured,
    deployment   = isConfigured ? foundryDeployment : "(set Foundry:Deployment in user secrets)",
    targetHost,
    apiVersion   = foundryApiVersion,
    utilityModel = utilityModelId
});

app.MapGet("/health", () => new
{
    status       = isConfigured ? "ok" : "not configured",
    proxy        = "FoundryProxy",
    configured   = isConfigured,
    deployment   = isConfigured ? foundryDeployment : "(set Foundry:Deployment in user secrets)",
    targetHost,
    apiVersion   = foundryApiVersion,
    utilityModel = utilityModelId,
    hint         = isConfigured
        ? null
        : "Run: dotnet user-secrets set \"Foundry:Endpoint\" \"...\" && dotnet user-secrets set \"Foundry:ApiKey\" \"...\" && dotnet user-secrets set \"Foundry:Deployment\" \"...\""
});

// --- 3b. Models list -------------------------------------------------------
// VS Code and many OpenAI-compatible clients call GET /v1/models before
// sending a chat request to confirm the model ID exists.  We return:
//
//   1. Every configured Azure deployment (Foundry:Deployments) — or the single
//      default deployment if no list is configured — each of which can be
//      selected in VS Code and is routed to the deployment of the same name.
//   2. The utility alias (e.g. copilot-utility-small) — needed so VS Code
//      accepts it as a valid BYOK candidate for the utility model slot.
//
// The requested model id becomes the Azure deployment (URL path); the utility
// alias / unknown ids are routed to the default deployment.
app.MapGet("/v1/models", () =>
{
    var ids = new List<string>();

    if (deploymentAllowlist.Count > 0)
        ids.AddRange(configuredDeployments);

    // Always include the default deployment so it is selectable too.
    if (isConfigured && !ids.Any(x => string.Equals(x, foundryDeployment, StringComparison.OrdinalIgnoreCase)))
        ids.Add(foundryDeployment);

    if (ids.Count == 0)
        ids.Add("not-configured");

    var data = new List<object>();
    foreach (var id in ids)
    {
        data.Add(new
        {
            id,
            @object  = "model",
            created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            owned_by = "azure-openai"
        });
    }

    // Utility alias — synthetic ID VS Code uses for the background utility slot.
    // Not a real deployment; the POST handler routes it to the default deployment.
    data.Add(new
    {
        id       = utilityModelId,
        @object  = "model",
        created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        owned_by = "azure-openai"
    });

    return Results.Json(new { @object = "list", data });
});

// --- 3c. Chat completions — THE CORE PROXY --------------------------------
//
//  This endpoint is what VS Code BYOK points to.  It:
//   1. Reads and parses the incoming OpenAI-style request body ONCE.
//   2. LOGS a model rewrite notice if the body carries a utility alias —
//      the alias is harmless for Azure (the deployment is in the URL path,
//      not the body), but logging it makes the utility-slot traffic visible
//      on stage.  We also set body["model"] = deployment for cleanliness.
//   3. Uses CopilotMessageExtractor to pull out what the user actually typed
//      and logs it — this is the "observe the ask" teaching moment.
//   4. Forwards the request to Azure OpenAI with the api-key header.
//      (Azure uses "api-key: <key>", NOT "Authorization: Bearer <key>".)
//   5. Streams the SSE bytes back if the request is a streaming request,
//      otherwise forwards the JSON response as-is.
app.MapPost("/v1/chat/completions", async (HttpRequest request, HttpResponse response,
    IHttpClientFactory clientFactory, ILogger<Program> logger) =>
{
    // ------------------------------------------------------------------
    // UNCONFIGURED GUARD
    // Fail fast with a friendly message when secrets are missing.
    // /health still responds (above) so the presenter can diagnose quickly.
    // ------------------------------------------------------------------
    if (!isConfigured)
    {
        response.StatusCode = 503;
        response.ContentType = "application/json; charset=utf-8";
        await response.WriteAsync("""
            {"error":"FoundryProxy is not configured. Set Foundry:Endpoint, Foundry:ApiKey, and Foundry:Deployment via dotnet user-secrets. See the README for the exact commands."}
            """);
        return;
    }

    // ------------------------------------------------------------------
    // STEP 1: Buffer the request body so we can (a) parse it and
    //         (b) forward the (possibly modified) body to Azure.
    // ------------------------------------------------------------------
    string bodyText;
    using (var reader = new StreamReader(request.Body))
    {
        bodyText = await reader.ReadToEndAsync();
    }

    // ------------------------------------------------------------------
    // STEP 2: PARSE ONCE — observe the ask, detect streaming, AND
    //         handle the model field.
    //
    //  We parse the JSON body a SINGLE time and reuse the JsonObject for
    //  all three tasks, avoiding redundant deserialization overhead.
    //
    //  --- ROUTING THE MODEL TO A DEPLOYMENT — the Azure nuance ---
    //
    //  For Ollama the "model" field in the body tells Ollama WHICH model to
    //  run.  For Azure OpenAI the DEPLOYMENT is encoded in the URL PATH:
    //    openai/deployments/{deployment}/chat/completions
    //  Azure ignores the body "model" field entirely.
    //
    //  So to honor the model the user picked (parity with the Ollama sample)
    //  we use the requested model id AS the deployment name when we build the
    //  URL.  That is what lets ONE proxy serve MANY Foundry deployments:
    //    • requested id is a real deployment → route to it (pass-through)
    //    • utility alias / unknown id         → route to the default deployment
    //  (see the deployment-resolution block below).  We also set
    //  body["model"] = the resolved deployment for cleanliness so logs and
    //  response inspection show the real deployment name.
    // ------------------------------------------------------------------
    bool   isStreaming        = false;
    string requestedModel     = foundryDeployment;
    string resolvedDeployment = foundryDeployment; // the Azure deployment we'll hit

    try
    {
        var jsonBody = JsonNode.Parse(bodyText) as JsonObject;
        if (jsonBody is not null)
        {
            // ---- Observe the ask ----
            // CopilotMessageExtractor peels away the Copilot Chat XML
            // envelope so we see only the words the user actually typed,
            // not kilobytes of <attachments>/<context>/etc. boilerplate.
            // In a REAL harness this extracted text drives routing, policy
            // filters, cost attribution, and intent classification.
            var typedAsk = CopilotMessageExtractor.GetLastUserMessageText(jsonBody);
            logger.LogInformation("[copilot ask] {TypedAsk}", typedAsk);
            Console.WriteLine($"[copilot ask] {typedAsk}");

            // ---- Detect streaming ----
            // OpenAI protocol: "stream": true → respond with SSE.
            // Copilot Chat always sets this; plain curl tests often don't.
            isStreaming = jsonBody["stream"]?.GetValue<bool>() ?? false;

            // ---- Resolve which Azure DEPLOYMENT to route to ----
            //
            //  Parity with the Ollama sample: honor the model the user picked.
            //  For Azure, "honoring" it means using the requested id AS the
            //  deployment name in the URL path (built below via BuildRequestUri).
            //
            //    • utility alias / empty                 → default deployment
            //    • allowlist configured & id not in it   → default deployment
            //    • otherwise                             → the requested id
            requestedModel = jsonBody["model"]?.GetValue<string>() ?? foundryDeployment;

            // Track whether we changed the body so we only re-serialize once.
            bool bodyModified = false;

            bool isUtilityAlias = string.Equals(requestedModel, utilityModelId, StringComparison.OrdinalIgnoreCase);
            bool notInAllowlist = deploymentAllowlist.Count > 0 && !deploymentAllowlist.Contains(requestedModel);

            if (string.IsNullOrWhiteSpace(requestedModel) || isUtilityAlias || notInAllowlist)
            {
                // Utility alias, empty, or an id we don't recognise → fall back
                // to the default deployment so the utility slot keeps working
                // and typos don't 404.  Log it so the traffic is visible on stage.
                resolvedDeployment = foundryDeployment;
                logger.LogInformation(
                    "[model rewrite] '{RequestedModel}' → deployment '{Deployment}' (utility/unknown id — routed to the default deployment)",
                    requestedModel, resolvedDeployment);
                Console.WriteLine($"[model rewrite] '{requestedModel}' → deployment '{resolvedDeployment}'");
            }
            else
            {
                // Real deployment id → route straight to it.  This is what lets
                // one proxy serve gpt-5.5, gpt-chat-latest, DeepSeek, etc.
                resolvedDeployment = requestedModel;
                logger.LogInformation(
                    "[model passthrough] '{RequestedModel}' (routed to the Azure deployment of the same name)",
                    requestedModel);
                Console.WriteLine($"[model passthrough] '{requestedModel}'");
            }

            // Reflect the resolved deployment in the body for cleanliness (Azure
            // ignores the body model, but logs/responses then show the truth).
            if (!string.Equals(jsonBody["model"]?.GetValue<string>(), resolvedDeployment, StringComparison.Ordinal))
            {
                jsonBody["model"] = resolvedDeployment;
                bodyModified = true;
            }

            // ---- Normalize unsupported sampling parameters ----
            //
            //  THE PROBLEM (seen live on stage):
            //    Newer Foundry models — the gpt-5.x family, gpt-chat-latest,
            //    the o-series, etc. — REJECT any custom "temperature" or
            //    "top_p". They only accept the default value of 1 and return:
            //      400 { "error": { "code": "unsupported_value",
            //            "message": "Unsupported value: 'temperature' does not
            //            support 0.1 with this model. Only the default (1) ..." }}
            //
            //  VS Code Copilot sends temperature: 0.1 on every request, so the
            //  call fails before it ever reaches the model.
            //
            //  THE FIX:
            //    Strip these fields when they are not the default. The model
            //    then applies its own default (1) and the request succeeds.
            //    The main router does the same thing behind its per-model
            //    "SupportsCustomTemperature" flag; here we keep it simple and
            //    always drop non-default values.
            //
            //    If you point this sample at an older model that DOES honor a
            //    custom temperature, flip StripUnsupportedSamplingParams to
            //    false in appsettings.json to forward the values untouched.
            if (stripUnsupportedSamplingParams)
            {
                foreach (var param in new[] { "temperature", "top_p" })
                {
                    // TryGetValue guards against a missing field or a non-numeric
                    // token — we only act on a real number that isn't the default.
                    if (jsonBody[param] is JsonValue node &&
                        node.TryGetValue<double>(out var value) &&
                        value != 1.0)
                    {
                        logger.LogInformation(
                            "[param strip] removed '{Param}'={Value} (this model only accepts the default of 1)",
                            param, value);
                        Console.WriteLine($"[param strip] removed '{param}'={value}");
                        jsonBody.Remove(param);
                        bodyModified = true;
                    }
                }
            }

            if (bodyModified)
            {
                bodyText = jsonBody.ToJsonString(); // forward the updated body
            }
        }
    }
    catch (JsonException)
    {
        // Malformed JSON — skip all parsing and let Azure return the error.
    }

    // LLM activity span — shows in the Aspire Traces tab as "llm.chat" with
    // tags: llm.proxy=FoundryProxy, llm.model, llm.streaming, llm.latency_ms.
    var llmSw   = System.Diagnostics.Stopwatch.StartNew();
    using var llmSpan = LlmActivity.StartChat("FoundryProxy", resolvedDeployment, isStreaming);

    // ------------------------------------------------------------------
    // STEP 3: FORWARD TO AZURE OPENAI
    //
    //  We use the pre-configured "foundry" HttpClient (5-minute timeout).
    //  The target URL is computed PER REQUEST from the resolved deployment
    //  (BuildRequestUri) — that is how one proxy serves many deployments.
    //
    //  AUTH: Azure OpenAI uses the "api-key" request header.
    //  This is NOT a Bearer token — do NOT use "Authorization: Bearer ...".
    //  This distinction matters on stage: the same raw HTTP approach used
    //  in ChatCompletionsProvider.cs in the main project.
    //
    //  IMPORTANT: always use bodyText here — it may have been updated
    //  in STEP 2.  Never re-read request.Body (it was consumed in STEP 1).
    // ------------------------------------------------------------------
    var httpClient = clientFactory.CreateClient("foundry");
    var foundryRequestUri = BuildRequestUri(resolvedDeployment);

    using var foundryRequest = new HttpRequestMessage(HttpMethod.Post, foundryRequestUri)
    {
        Content = new StringContent(bodyText, System.Text.Encoding.UTF8, "application/json")
    };

    // Azure OpenAI authentication header.
    // "api-key" is the Azure-specific header name — NOT "Authorization: Bearer".
    // The key comes from user secrets, never from the VS Code client.
    foundryRequest.Headers.Add("api-key", foundryApiKey);

    // For streaming we need to receive headers before the full body arrives.
    var completionOption = isStreaming
        ? HttpCompletionOption.ResponseHeadersRead
        : HttpCompletionOption.ResponseContentRead;

    HttpResponseMessage foundryResponse;
    try
    {
        foundryResponse = await httpClient.SendAsync(foundryRequest, completionOption);
    }
    catch (TaskCanceledException)
    {
        // Timeout exceeded (>5 minutes) — very unusual but surface a clear message.
        LlmActivity.SetResult(llmSpan, llmSw.ElapsedMilliseconds, error: "timeout");
        response.StatusCode = 504;
        await response.WriteAsync("{\"error\":\"Azure OpenAI request timed out after 5 minutes.\"}");
        return;
    }
    catch (HttpRequestException ex)
    {
        // Network failure — Azure endpoint unreachable.
        LlmActivity.SetResult(llmSpan, llmSw.ElapsedMilliseconds, error: ex.Message);
        response.StatusCode = 502;
        await response.WriteAsync($"{{\"error\":\"Could not reach Azure OpenAI at {targetHost}: {ex.Message}\"}}");
        return;
    }

    // ------------------------------------------------------------------
    // STEP 4: RELAY THE RESPONSE BACK TO THE CALLER
    //
    //  Streaming path: copy the SSE byte-stream as it arrives so the
    //  caller (VS Code Copilot Chat) sees tokens appear in real time.
    //
    //  Non-streaming path: read the full body and return it as JSON.
    //
    //  Error paths: surface friendly hints for common Azure auth failures.
    // ------------------------------------------------------------------
    response.StatusCode = (int)foundryResponse.StatusCode;

    // Friendly hint for auth/config errors — common on first run.
    if (foundryResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                   or System.Net.HttpStatusCode.Forbidden)
    {
        response.ContentType = "application/json; charset=utf-8";
        var errorBody = await foundryResponse.Content.ReadAsStringAsync();
        logger.LogWarning(
            "[foundry auth error] {StatusCode} — check Foundry:ApiKey, Foundry:Endpoint, and Foundry:Deployment in user secrets. Azure response: {Body}",
            foundryResponse.StatusCode, errorBody);
        await response.WriteAsync($"{{\"error\":\"Azure returned {(int)foundryResponse.StatusCode}. " +
            "Check that Foundry:ApiKey, Foundry:Endpoint, and Foundry:Deployment are correct in user secrets. " +
            $"Azure message: {JsonSerializer.Serialize(errorBody)}\"}}");
        return;
    }

    if (isStreaming)
    {
        // Streaming: set SSE content-type and pipe bytes straight through.
        // DO NOT buffer — that would defeat the purpose of streaming.
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"]     = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no"; // tell nginx not to buffer either

        await using var foundryStream = await foundryResponse.Content.ReadAsStreamAsync();
        await foundryStream.CopyToAsync(response.Body);
    }
    else
    {
        // Non-streaming: return the full JSON response.
        response.ContentType = "application/json; charset=utf-8";
        var json = await foundryResponse.Content.ReadAsStringAsync();
        await response.WriteAsync(json);
    }

    llmSw.Stop();
    LlmActivity.SetResult(llmSpan, llmSw.ElapsedMilliseconds);
});

// ---------------------------------------------------------------------------
// 4. RUN ON THE FIXED PORT
// ---------------------------------------------------------------------------
// Port 5100 is documented in the README and in the VS Code BYOK snippet.
// OllamaProxy uses 5099 — keeping them on separate ports so both proxies
// can run simultaneously (useful for the full harness demo where the router
// switches between local Ollama and cloud Foundry based on policy).
// Port is configured via Properties/launchSettings.json (standalone dotnet run)
// or via ASPNETCORE_URLS set by the Aspire AppHost (proxies/AppHost).
// Both pin to 5100 so VS Code BYOK settings never need to change.
app.Run();
