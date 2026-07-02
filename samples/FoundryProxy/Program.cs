// =============================================================================
//  FoundryProxy — minimal OpenAI-compatible proxy for Azure AI Foundry /
//                 Azure OpenAI.
//
//  PURPOSE (teaching sample)
//  -------------------------
//  This is intentionally the SMALLEST possible proxy that lets VS Code Copilot
//  treat an Azure AI Foundry (Azure OpenAI) deployment as a BYOK (Bring Your
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

// ---------------------------------------------------------------------------
// 1. BUILDER — wire up services before building the app
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

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
//     • POST /v1/chat/completions — if the request arrives with model =
//       utilityModelId (e.g. "copilot-utility-small"), LOG the rewrite.
//       For Azure the deployment is specified in the URL path, not the
//       body, so all requests already hit the same deployment regardless
//       of what "model" field the body carries.  We optionally set
//       body["model"] = deployment for cleanliness (see STEP 2 below).
//
//   NOTE: a single Foundry deployment serves both the main AND utility
//   slots in this simple sample.  In production you would configure two
//   separate deployments (e.g. a large model for chat, a small/fast one
//   for utility tasks) and route by model ID.
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

// The full URL used for every chat completions request:
//   {resourceBase}openai/deployments/{deployment}/chat/completions?api-version={apiVersion}
var foundryRequestUri = isConfigured
    ? $"{resourceBase}openai/deployments/{Uri.EscapeDataString(foundryDeployment)}/chat/completions?api-version={Uri.EscapeDataString(foundryApiVersion)}"
    : string.Empty;

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
// sending a chat request to confirm the model ID exists.  We return TWO
// entries:
//
//   1. The Azure deployment name (e.g. gpt-4o-mini) — used for main chat.
//   2. The utility alias (e.g. copilot-utility-small) — needed so VS Code
//      accepts it as a valid BYOK candidate for the utility model slot.
//
// Both IDs route to the SAME underlying Azure deployment at inference time.
// For Azure the deployment is in the URL path, so the body's "model" field
// does not change which deployment is invoked.
app.MapGet("/v1/models", () =>
{
    var modelsResponse = new
    {
        @object = "list",
        data = new object[]
        {
            new
            {
                id       = isConfigured ? foundryDeployment : "not-configured",
                @object  = "model",
                created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                owned_by = "azure-openai"
            },
            // Utility alias — same underlying Azure deployment, synthetic ID.
            // VS Code needs to see this ID here so it trusts it as a valid
            // BYOK model when the user sets chat.utilitySmallModel.
            new
            {
                id       = utilityModelId,
                @object  = "model",
                created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                owned_by = "azure-openai"
            }
        }
    };
    return Results.Json(modelsResponse);
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
    //  --- THE MODEL FIELD FOR AZURE — an important nuance ---
    //
    //  For Ollama the "model" field in the body tells Ollama WHICH model
    //  to run — so OllamaProxy MUST rewrite it.
    //
    //  For Azure OpenAI the deployment is encoded in the URL path:
    //    openai/deployments/{deployment}/chat/completions
    //  Azure ignores the body "model" field entirely.  So even if Copilot's
    //  agent surface sends model="copilot-utility-small", the request still
    //  hits our chosen deployment.
    //
    //  We STILL log the rewrite so the audience can see the utility-slot
    //  traffic, and we set body["model"] = deployment for cleanliness so
    //  any logging or response inspection shows the real deployment name.
    // ------------------------------------------------------------------
    bool   isStreaming    = false;
    string requestedModel = foundryDeployment;

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

            // ---- Handle model field ----
            requestedModel = jsonBody["model"]?.GetValue<string>() ?? foundryDeployment;

            if (!string.Equals(requestedModel, foundryDeployment, StringComparison.OrdinalIgnoreCase))
            {
                // The request arrived with a non-deployment model ID (e.g. the
                // utility model alias "copilot-utility-small").
                //
                // KEY POINT FOR AZURE: the deployment is in the URL path, so
                // this model ID has NO effect on which deployment is invoked.
                // We log the rewrite to make the utility-slot traffic visible
                // on stage, and we update the body field so logs/responses show
                // the real deployment name.
                logger.LogInformation(
                    "[model rewrite] '{RequestedModel}' → deployment '{Deployment}' (Azure: deployment is in URL path, body model field is cosmetic)",
                    requestedModel, foundryDeployment);
                Console.WriteLine($"[model rewrite] '{requestedModel}' → deployment '{foundryDeployment}'");

                jsonBody["model"] = foundryDeployment;
                bodyText = jsonBody.ToJsonString(); // forward the updated body
            }
        }
    }
    catch (JsonException)
    {
        // Malformed JSON — skip all parsing and let Azure return the error.
    }

    // ------------------------------------------------------------------
    // STEP 3: FORWARD TO AZURE OPENAI
    //
    //  We use the pre-configured "foundry" HttpClient (5-minute timeout).
    //  The full target URL was computed at startup from the user secrets.
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
        response.StatusCode = 504;
        await response.WriteAsync("{\"error\":\"Azure OpenAI request timed out after 5 minutes.\"}");
        return;
    }
    catch (HttpRequestException ex)
    {
        // Network failure — Azure endpoint unreachable.
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
});

// ---------------------------------------------------------------------------
// 4. RUN ON THE FIXED PORT
// ---------------------------------------------------------------------------
// Port 5100 is documented in the README and in the VS Code BYOK snippet.
// OllamaProxy uses 5099 — keeping them on separate ports so both proxies
// can run simultaneously (useful for the full harness demo where the router
// switches between local Ollama and cloud Foundry based on policy).
app.Run("http://localhost:5100");
