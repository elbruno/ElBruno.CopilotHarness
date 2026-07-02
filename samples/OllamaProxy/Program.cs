// =============================================================================
//  OllamaProxy — minimal OpenAI-compatible proxy for a local Ollama instance.
//
//  PURPOSE (teaching sample)
//  -------------------------
//  This is intentionally the SMALLEST possible proxy that lets VS Code Copilot
//  treat a local Ollama model as a BYOK (Bring Your Own Key / Bring Your Own
//  Model) provider.  Every line is commented so it can be read on stage.
//
//  WHY A WEB APP AND NOT A CONSOLE APP?
//  -------------------------------------
//  BYOK in VS Code requires an HTTP endpoint.  VS Code's model-provider
//  registration ("chatLanguageModels.json") points to a URL such as
//  http://localhost:5099/v1/chat/completions.  A plain console app has no
//  TCP listener so the editor can't reach it.  ASP.NET Core Minimal API gives
//  us that listener with almost zero ceremony — the startup code is ~10 lines.
//
//  FIXED PORT: http://localhost:5099
//  All README instructions and the VS Code snippet use this port.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using OllamaProxy;

// ---------------------------------------------------------------------------
// 1. BUILDER — wire up services before building the app
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Read Ollama settings from appsettings.json (or environment overrides).
// Defaults are safe for a stock Ollama installation.
var ollamaBaseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
// The DEFAULT/FALLBACK model.  This is used ONLY as the remap target when a
// request arrives for the synthetic utility alias or an unknown model id.
// Real model ids that Ollama has installed are passed through untouched (see
// the POST handler below).  If no explicit value is configured we fall back to
// the first installed Ollama model, then to "llama3.1:8b".
// (defaultModel itself is computed after the app is built — see section 2b —
//  because it may depend on the list of installed models.)
var configuredDefaultModel = builder.Configuration["Ollama:DefaultModel"];

// WHY A UTILITY MODEL ID?
//   GitHub Copilot's AGENT surface (the "Describe what to build" input in
//   VS Code) uses TWO separate model "slots" at the same time:
//
//     1. MAIN model   — the model the user selected in the model picker
//                       (e.g. llama3.1:8b).  Handles the user's chat turns.
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
//     • GET  /v1/models  — advertises BOTH the main model ID AND this
//       utility ID so VS Code discovers a valid utility candidate.
//     • POST /v1/chat/completions — forwards the requested model UNCHANGED
//       when Ollama has it installed (so the user's pick is honored), and
//       REWRITES it to defaultModel only for the utility alias / unknown ids
//       (Ollama would 404 on those synthetic aliases).
var utilityModelId = builder.Configuration["Ollama:UtilityModelId"] ?? "copilot-utility-small";

// Register a named HttpClient for forwarding requests to Ollama.
//
// WHY A LONG TIMEOUT?
//   HttpClient's default timeout is 100 seconds.  LLM responses — especially
//   non-streaming ones waiting for a full completion — regularly exceed this for
//   large prompts.  Set it to something generous (5 min here) so the proxy
//   doesn't kill the connection mid-generation.
//   Streaming responses start flowing quickly, but the *connection* still needs
//   to stay open until the model finishes, so the same long timeout applies.
builder.Services.AddHttpClient("ollama", client =>
{
    client.BaseAddress = new Uri(ollamaBaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5); // LLM responses can be SLOW — don't timeout early
});

// ---------------------------------------------------------------------------
// 2. BUILD THE APP — configure fixed port (5099) via Kestrel
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// 2b. DISCOVER INSTALLED OLLAMA MODELS (the pass-through allowlist)
//
//  We ask Ollama which models are actually installed locally (GET /api/tags)
//  ONE TIME at startup and cache the names.  This list is the "pass-through
//  allowlist": when VS Code sends a chat request for one of THESE ids, we
//  forward it to Ollama UNCHANGED so the user's model pick is honored.  Any id
//  NOT in this list (the synthetic utility alias, a typo, etc.) is remapped to
//  defaultModel so Ollama doesn't 404.
//
//  WHY AT STARTUP?  It keeps the per-request hot path fast and the teaching
//  code simple.  Pulled a new model with `ollama pull <name>`?  Restart the
//  proxy to pick it up.  A production harness would cache with a short TTL.
// ---------------------------------------------------------------------------
var installedModels = new List<string>();
try
{
    var probe = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("ollama");
    // /api/tags is Ollama's native "list installed models" endpoint.
    // Shape: { "models": [ { "name": "llama3.1:8b", ... }, ... ] }
    using var tagsResponse = await probe.GetAsync("/api/tags");
    if (tagsResponse.IsSuccessStatusCode)
    {
        var tagsJson = await tagsResponse.Content.ReadAsStringAsync();
        if (JsonNode.Parse(tagsJson)?["models"] is JsonArray modelsArray)
        {
            foreach (var m in modelsArray)
            {
                var name = m?["name"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                    installedModels.Add(name);
            }
        }
    }
}
catch (Exception ex)
{
    // Ollama not running yet, or unreachable.  Degrade gracefully: with an
    // empty allowlist EVERY request falls back to defaultModel (the original
    // single-model behavior), so the proxy still works — it just can't pass
    // models through until Ollama is up and the proxy is restarted.
    app.Logger.LogWarning(
        "Could not list Ollama models at startup: {Message}. Model pass-through disabled until restart.",
        ex.Message);
}

// Case-insensitive set for fast pass-through checks on the request hot path.
var installedModelSet = new HashSet<string>(installedModels, StringComparer.OrdinalIgnoreCase);

// Resolve the remap/fallback target: explicit config → first installed model → default.
var defaultModel = configuredDefaultModel
    ?? installedModels.FirstOrDefault()
    ?? "llama3.1:8b";

app.Logger.LogInformation(
    "Installed Ollama models: {Models}. Remap/fallback model: {Default}",
    installedModels.Count > 0 ? string.Join(", ", installedModels) : "(none discovered)",
    defaultModel);

// ---------------------------------------------------------------------------
// 3. ENDPOINTS
// ---------------------------------------------------------------------------

// --- 3a. Health / info endpoint -------------------------------------------
// A quick "is it alive?" check.  Open http://localhost:5099/ in a browser
// or run: curl http://localhost:5099/health
app.MapGet("/", () => new { status = "ok", proxy = "OllamaProxy", model = defaultModel, utilityModel = utilityModelId, ollamaUrl = ollamaBaseUrl });
app.MapGet("/health", () => new { status = "ok", proxy = "OllamaProxy", model = defaultModel, utilityModel = utilityModelId, ollamaUrl = ollamaBaseUrl });

// --- 3b. Models list -------------------------------------------------------
// VS Code and many OpenAI-compatible clients call GET /v1/models before
// sending a chat request to confirm the model ID exists.  We return:
//
//   1. Every model Ollama actually has installed (discovered at startup) —
//      any of these can be selected in VS Code and is forwarded as-is.
//   2. The utility alias (e.g. copilot-utility-small) — needed so VS Code
//      accepts it as a valid BYOK candidate for the utility model slot.
//
// Real model IDs run on their own Ollama model; the utility alias is remapped
// to defaultModel by the POST endpoint (Ollama has no such model).
app.MapGet("/v1/models", () =>
{
    var data = new List<object>();

    // Advertise every installed model (or defaultModel if discovery found none).
    var advertised = installedModelSet.Count > 0
        ? (IEnumerable<string>)installedModels
        : new[] { defaultModel };

    foreach (var id in advertised)
    {
        data.Add(new
        {
            id,
            @object  = "model",
            created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            owned_by = "ollama"
        });
    }

    // Utility alias — synthetic ID VS Code uses for the background utility slot.
    // Not a real Ollama model; the POST handler remaps it to defaultModel.
    data.Add(new
    {
        id       = utilityModelId,
        @object  = "model",
        created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        owned_by = "ollama"
    });

    return Results.Json(new { @object = "list", data });
});

// --- 3c. Chat completions — THE CORE PROXY --------------------------------
//
//  This endpoint is what VS Code BYOK points to.  It:
//   1. Reads and parses the incoming OpenAI-style request body ONCE.
//   2. REWRITES the "model" field if it is a utility alias — translating
//      e.g. "copilot-utility-small" → "llama3.1:8b" before forwarding.
//      Ollama would 404 on any unknown model ID, so this rewrite is essential
//      for the agent surface to work when only one local model is running.
//   3. Uses CopilotMessageExtractor to pull out what the user actually typed
//      and logs it — this is the "observe the ask" teaching moment.
//   4. Forwards the (possibly rewritten) request body to Ollama.
//   5. Streams the SSE bytes back if the request is a streaming request,
//      otherwise returns the JSON response as-is.
app.MapPost("/v1/chat/completions", async (HttpRequest request, HttpResponse response,
    IHttpClientFactory clientFactory, ILogger<Program> logger) =>
{
    // ------------------------------------------------------------------
    // STEP 1: Buffer the request body so we can (a) parse it and
    //         (b) forward the (possibly modified) body to Ollama.
    // ------------------------------------------------------------------
    string bodyText;
    using (var reader = new StreamReader(request.Body))
    {
        bodyText = await reader.ReadToEndAsync();
    }

    // ------------------------------------------------------------------
    // STEP 2: PARSE ONCE — observe the ask, detect streaming, AND
    //         rewrite the model field if needed.
    //
    //  We parse the JSON body a SINGLE time and reuse the JsonObject for
    //  all three tasks, avoiding redundant deserialization overhead.
    //
    //  --- MODEL PASS-THROUGH vs REMAP — why it exists ---
    //
    //  Copilot's agent surface sends TWO distinct kinds of requests:
    //
    //    a) Main model turns  — model = the id the user PICKED in the model
    //       picker (e.g. "llama3.1:8b" OR "qwen2.5:7b-instruct").  These are
    //       the user's actual chat messages, and we want to honor that choice.
    //
    //    b) Utility turns     — model = "copilot-utility-small"
    //       Background tasks VS Code runs silently: generating a title for
    //       the chat session, suggesting a commit message, producing rename
    //       hints, etc.  The user never explicitly triggers these.
    //
    //  Solution:
    //    • If the requested model IS installed in Ollama (case a) → forward it
    //      UNCHANGED.  This is what lets ONE proxy serve MANY Ollama models:
    //      whatever the user selects in VS Code is what actually runs.
    //    • If it is NOT installed (case b — the synthetic utility alias, or a
    //      typo) → remap it to defaultModel, because Ollama would otherwise
    //      return HTTP 404 and the agent surface shows:
    //
    //        "No utility model is configured for 'copilot-utility-small'
    //         while the selected main model is BYOK."
    // ------------------------------------------------------------------
    bool isStreaming = false;
    string requestedModel = defaultModel;

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

            // ---- Honor the requested model (pass-through) OR remap it ----
            //
            //  VS Code sends model = the id the user picked in the model picker.
            //  If Ollama actually has that model installed, we PASS IT THROUGH
            //  untouched so the user's choice runs.  If not (utility alias or an
            //  unknown id), we REMAP to defaultModel so Ollama doesn't 404.
            requestedModel = jsonBody["model"]?.GetValue<string>() ?? defaultModel;

            if (installedModelSet.Contains(requestedModel))
            {
                // Real, installed model — forward as-is.  Log so the audience
                // can see the user's pick flowing straight to Ollama.
                logger.LogInformation(
                    "[model passthrough] '{RequestedModel}' (installed — forwarded unchanged)",
                    requestedModel);
                Console.WriteLine($"[model passthrough] '{requestedModel}'");
            }
            else
            {
                // Utility alias or unknown id — remap to a real model so Ollama
                // can serve it.  This is what keeps the agent surface's utility
                // slot working.  Log the rewrite so it's visible on stage.
                logger.LogInformation(
                    "[model rewrite] '{RequestedModel}' → '{DefaultModel}' (not installed — remapped so Ollama can serve it)",
                    requestedModel, defaultModel);
                Console.WriteLine($"[model rewrite] '{requestedModel}' → '{defaultModel}'");

                jsonBody["model"] = defaultModel;
                bodyText = jsonBody.ToJsonString(); // forward the modified body
            }
        }
    }
    catch (JsonException)
    {
        // Malformed JSON — skip all parsing and let Ollama return the error.
    }

    // ------------------------------------------------------------------
    // STEP 3: FORWARD TO OLLAMA
    //
    //  We use the pre-configured "ollama" HttpClient (5-minute timeout).
    //  The target is Ollama's OpenAI-compatible endpoint which mirrors the
    //  exact same /v1/chat/completions path and request/response shapes.
    //
    //  IMPORTANT: always use bodyText here — it may have been rewritten
    //  in STEP 2.  Never re-read request.Body (it was consumed in STEP 1).
    // ------------------------------------------------------------------
    var httpClient = clientFactory.CreateClient("ollama");
    var ollamaUri = "/v1/chat/completions"; // relative to the BaseAddress set above

    using var ollamaRequest = new HttpRequestMessage(HttpMethod.Post, ollamaUri)
    {
        Content = new StringContent(bodyText, System.Text.Encoding.UTF8, "application/json")
    };

    // For streaming we need to receive headers before the full body arrives.
    var completionOption = isStreaming
        ? HttpCompletionOption.ResponseHeadersRead
        : HttpCompletionOption.ResponseContentRead;

    HttpResponseMessage ollamaResponse;
    try
    {
        ollamaResponse = await httpClient.SendAsync(ollamaRequest, completionOption);
    }
    catch (TaskCanceledException)
    {
        // Timeout exceeded (>5 minutes) — very unusual but surface a clear message.
        response.StatusCode = 504;
        await response.WriteAsync("{\"error\":\"Ollama request timed out after 5 minutes.\"}");
        return;
    }
    catch (HttpRequestException ex)
    {
        // Ollama not running or unreachable.
        response.StatusCode = 502;
        await response.WriteAsync($"{{\"error\":\"Could not reach Ollama at {ollamaBaseUrl}: {ex.Message}\"}}");
        return;
    }

    // ------------------------------------------------------------------
    // STEP 4: RELAY THE RESPONSE BACK TO THE CALLER
    //
    //  Streaming path: copy the SSE byte-stream as it arrives so the
    //  caller (VS Code Copilot Chat) sees tokens appear in real time.
    //
    //  Non-streaming path: read the full body and return it as JSON.
    // ------------------------------------------------------------------
    response.StatusCode = (int)ollamaResponse.StatusCode;

    if (isStreaming)
    {
        // Streaming: set SSE content-type and pipe bytes straight through.
        // DO NOT buffer — that would defeat the purpose of streaming.
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no"; // tell nginx not to buffer either

        await using var ollamaStream = await ollamaResponse.Content.ReadAsStreamAsync();
        await ollamaStream.CopyToAsync(response.Body);
    }
    else
    {
        // Non-streaming: return the full JSON response.
        response.ContentType = "application/json; charset=utf-8";
        var json = await ollamaResponse.Content.ReadAsStringAsync();
        await response.WriteAsync(json);
    }
});

// ---------------------------------------------------------------------------
// 4. RUN ON THE FIXED PORT
// ---------------------------------------------------------------------------
// Port 5099 is documented in the README and in the VS Code BYOK snippet.
// Using app.Run() with an explicit URL is the simplest approach for a sample.
app.Run("http://localhost:5099");
