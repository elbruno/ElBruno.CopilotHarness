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
var defaultModel  = builder.Configuration["Ollama:DefaultModel"] ?? "llama3.1:8b";

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
app.MapGet("/",      () => new { status = "ok", proxy = "OllamaProxy", model = defaultModel, ollamaUrl = ollamaBaseUrl });
app.MapGet("/health",() => new { status = "ok", proxy = "OllamaProxy", model = defaultModel, ollamaUrl = ollamaBaseUrl });

// --- 3b. Models list -------------------------------------------------------
// VS Code and many OpenAI-compatible clients call GET /v1/models before
// sending a chat request to confirm the model ID exists.  We return a minimal
// static response containing the configured model so the probe succeeds.
app.MapGet("/v1/models", () =>
{
    var modelsResponse = new
    {
        @object = "list",
        data = new[]
        {
            new
            {
                id      = defaultModel,
                @object = "model",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                owned_by = "ollama"
            }
        }
    };
    return Results.Json(modelsResponse);
});

// --- 3c. Chat completions — THE CORE PROXY --------------------------------
//
//  This endpoint is what VS Code BYOK points to.  It:
//   1. Reads and parses the incoming OpenAI-style request body.
//   2. Uses CopilotMessageExtractor to pull out what the user actually typed
//      and logs it — this is the "observe the ask" teaching moment.
//   3. Forwards the original (unmodified) request body to Ollama.
//   4. Streams the SSE bytes back if the request is a streaming request,
//      otherwise returns the JSON response as-is.
//
//  Pure proxy + observe — we don't change the forwarded body at all.
app.MapPost("/v1/chat/completions", async (HttpRequest request, HttpResponse response,
    IHttpClientFactory clientFactory, ILogger<Program> logger) =>
{
    // ------------------------------------------------------------------
    // STEP 1: Buffer the request body so we can (a) parse it for logging
    //         and (b) forward it unchanged to Ollama.
    // ------------------------------------------------------------------
    string bodyText;
    using (var reader = new StreamReader(request.Body))
    {
        bodyText = await reader.ReadToEndAsync();
    }

    // ------------------------------------------------------------------
    // STEP 2: OBSERVE THE ASK — extract and log what the user typed.
    //
    //  This is the key teaching moment.
    //  CopilotMessageExtractor peels away the Copilot Chat XML envelope
    //  (<attachments>, <context>, <userRequest>, etc.) so we see the
    //  actual words, not kilobytes of boilerplate.
    //
    //  In a REAL harness (ElBruno.CopilotHarness) this is the spot where
    //  you would:
    //    • Route the request to a different model based on the ask
    //    • Apply content policies or safety filters
    //    • Record telemetry / cost-attribution per user/team
    //    • Classify intent for smarter model selection
    //  For this sample we ONLY log — no forwarded body is modified.
    // ------------------------------------------------------------------
    try
    {
        var jsonBody = JsonNode.Parse(bodyText) as JsonObject;
        if (jsonBody is not null)
        {
            var typedAsk = CopilotMessageExtractor.GetLastUserMessageText(jsonBody);
            // Log to the console so the audience can see what Copilot asked:
            logger.LogInformation("[copilot ask] {TypedAsk}", typedAsk);
            Console.WriteLine($"[copilot ask] {typedAsk}");
        }
    }
    catch (JsonException)
    {
        // Malformed JSON — skip logging and let Ollama return the error.
    }

    // ------------------------------------------------------------------
    // STEP 3: DETECT STREAMING
    //
    //  OpenAI protocol: if the request body contains "stream": true the
    //  server must respond with Server-Sent Events (SSE) — a sequence of
    //  "data: {...}\n\n" lines, terminated by "data: [DONE]\n\n".
    //
    //  Copilot Chat ALWAYS sets stream: true.  If we return a plain JSON
    //  blob the editor will stall waiting for an SSE stream that never
    //  starts.  So we MUST detect the flag and copy the SSE stream through.
    // ------------------------------------------------------------------
    bool isStreaming = false;
    try
    {
        var peek = JsonNode.Parse(bodyText) as JsonObject;
        isStreaming = peek?["stream"]?.GetValue<bool>() ?? false;
    }
    catch (JsonException) { /* non-fatal */ }

    // ------------------------------------------------------------------
    // STEP 4: FORWARD TO OLLAMA
    //
    //  We use the pre-configured "ollama" HttpClient (5-minute timeout).
    //  The target is Ollama's OpenAI-compatible endpoint which mirrors the
    //  exact same /v1/chat/completions path and request/response shapes.
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
    // STEP 5: RELAY THE RESPONSE BACK TO THE CALLER
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
