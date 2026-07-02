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
var ollamaBaseUrl  = builder.Configuration["Ollama:BaseUrl"]        ?? "http://localhost:11434";
var defaultModel   = builder.Configuration["Ollama:DefaultModel"]   ?? "llama3.1:8b";

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
//     • POST /v1/chat/completions — if the request arrives with model =
//       utilityModelId (e.g. "copilot-utility-small"), REWRITE it to
//       defaultModel before forwarding.  Ollama only knows llama3.1:8b;
//       it would 404 on any synthetic alias.
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
// 3. ENDPOINTS
// ---------------------------------------------------------------------------

// --- 3a. Health / info endpoint -------------------------------------------
// A quick "is it alive?" check.  Open http://localhost:5099/ in a browser
// or run: curl http://localhost:5099/health
app.MapGet("/",      () => new { status = "ok", proxy = "OllamaProxy", model = defaultModel, utilityModel = utilityModelId, ollamaUrl = ollamaBaseUrl });
app.MapGet("/health",() => new { status = "ok", proxy = "OllamaProxy", model = defaultModel, utilityModel = utilityModelId, ollamaUrl = ollamaBaseUrl });

// --- 3b. Models list -------------------------------------------------------
// VS Code and many OpenAI-compatible clients call GET /v1/models before
// sending a chat request to confirm the model ID exists.  We return TWO
// entries:
//
//   1. The real Ollama model (e.g. llama3.1:8b) — used for main chat turns.
//   2. The utility alias (e.g. copilot-utility-small) — needed so VS Code
//      accepts it as a valid BYOK candidate for the utility model slot.
//
// Both IDs map to the SAME underlying Ollama model at inference time.
// The POST endpoint rewrites any utility alias back to defaultModel.
app.MapGet("/v1/models", () =>
{
    var modelsResponse = new
    {
        @object = "list",
        data = new object[]
        {
            new
            {
                id       = defaultModel,
                @object  = "model",
                created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                owned_by = "ollama"
            },
            // Utility alias — same underlying model, synthetic ID.
            // VS Code needs to see this ID here so it trusts it as a valid
            // BYOK model when the user sets chat.utilitySmallModel.
            new
            {
                id       = utilityModelId,
                @object  = "model",
                created  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                owned_by = "ollama"
            }
        }
    };
    return Results.Json(modelsResponse);
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
    //  --- THE MODEL REWRITE — why it exists ---
    //
    //  Copilot's agent surface sends TWO distinct kinds of requests:
    //
    //    a) Main model turns  — model = "llama3.1:8b"
    //       These are the user's actual chat messages.
    //
    //    b) Utility turns     — model = "copilot-utility-small"
    //       Background tasks VS Code runs silently: generating a title for
    //       the chat session, suggesting a commit message, producing rename
    //       hints, etc.  The user never explicitly triggers these.
    //
    //  Ollama only has model (a) installed locally.  If we forward request
    //  (b) unchanged, Ollama returns HTTP 404 "model not found" and the
    //  agent surface surfaces the error:
    //
    //    "No utility model is configured for 'copilot-utility-small'
    //     while the selected main model is BYOK."
    //
    //  Solution: detect any non-default model ID and silently remap it to
    //  defaultModel before forwarding.  All IDs this proxy advertises via
    //  GET /v1/models are just aliases — they all run on the same Ollama
    //  model.  Ollama never sees the synthetic alias.
    // ------------------------------------------------------------------
    bool   isStreaming    = false;
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

            // ---- Rewrite model ID ----
            requestedModel = jsonBody["model"]?.GetValue<string>() ?? defaultModel;

            if (!string.Equals(requestedModel, defaultModel, StringComparison.OrdinalIgnoreCase))
            {
                // The request arrived with a synthetic alias (e.g. the
                // utility model ID).  Replace it with the real Ollama model
                // so Ollama can handle it.  Log the rewrite so the audience
                // can see exactly what the agent surface is doing.
                logger.LogInformation(
                    "[model rewrite] '{RequestedModel}' → '{DefaultModel}' (utility alias remapped to Ollama model)",
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
    var ollamaUri  = "/v1/chat/completions"; // relative to the BaseAddress set above

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
        response.Headers["Cache-Control"]    = "no-cache";
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
