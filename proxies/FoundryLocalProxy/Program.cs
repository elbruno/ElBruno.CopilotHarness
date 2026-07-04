// =============================================================================
//  FoundryLocalProxy — minimal OpenAI-compatible proxy for Microsoft Foundry Local.
//
//  PURPOSE (teaching sample)
//  -------------------------
//  This is intentionally the SMALLEST possible proxy that lets VS Code Copilot
//  treat a local Microsoft Foundry Local model as a BYOK (Bring Your Own Key /
//  Bring Your Own Model) provider.  Every line is commented so it can be read
//  on stage.
//
//  WHAT IS FOUNDRY LOCAL?
//  ----------------------
//  Microsoft Foundry Local (https://github.com/microsoft/Foundry-Local) is a
//  free, offline model runtime that runs Phi, Llama, Mistral, Qwen, and other
//  open-weight models directly on your CPU, GPU, or NPU — no Azure account,
//  no internet connection, no per-token cost.
//
//  HOW DOES MODEL DOWNLOAD WORK?
//  ------------------------------
//  This proxy uses the official Foundry Local C# SDK (Microsoft.AI.Foundry.Local).
//  The SDK handles EVERYTHING — daemon, model downloads, hardware optimisation,
//  and an internal OpenAI-compatible REST server — with NO CLI required.
//
//  At startup the proxy:
//    1. Initialises the SDK (starts the Foundry Local daemon if needed).
//    2. Downloads GPU/NPU execution providers for optimal hardware use.
//    3. Downloads the DefaultModel if not already in the local cache
//       (phi-4-mini ≈ 2.5 GB on first run; instant on subsequent runs).
//    4. Loads the model into memory and starts the SDK's internal REST server.
//    5. Discovers which models are now loaded and builds the allowlist.
//    6. Starts accepting VS Code BYOK requests on port 5101.
//
//  The SDK's DownloadAsync() SKIPS the download if the model is already cached,
//  so subsequent proxy starts are near-instant.
//
//  ARCHITECTURE
//  ------------
//  VS Code Copilot → FoundryLocalProxy :5101 → SDK REST server :55588 → phi-4-mini
//
//  The proxy layer exists because:
//    • VS Code BYOK needs a stable, known port (5101).
//    • The proxy adds Copilot-specific logging ([copilot ask]).
//    • The proxy handles the utility model alias rewrite.
//    • The proxy is the BYOK teaching artefact — the SDK is the engine.
//
//  ADDITIONAL MODELS
//  -----------------
//  Set FoundryLocal:AdditionalModels in appsettings.json to load more models.
//  All loaded models become selectable in VS Code via this one proxy endpoint.
//  The SDK automatically picks the best hardware variant for each.
//
//  PRE-REQUISITES
//  --------------
//  None beyond .NET 10!  The SDK takes care of everything else.
//  On first run you need an internet connection to download the model weights.
//  After that the proxy works fully offline.
//
//  FIXED PORT: http://localhost:5101
//  OllamaProxy uses 5099, FoundryProxy uses 5100 — all three can run together.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using FoundryLocalProxy;
using Microsoft.AI.Foundry.Local;
using Proxies.ServiceDefaults;

// ---------------------------------------------------------------------------
// 1. BUILDER — wire up services before building the app
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// AddProxiesServiceDefaults wires OpenTelemetry (traces + metrics + logs) and
// sends data to Aspire via OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set.
builder.AddProxiesServiceDefaults();

// Read settings from appsettings.json (or environment overrides).
var defaultModelAlias = builder.Configuration["FoundryLocal:DefaultModel"] ?? "phi-4-mini";

var additionalModels = builder.Configuration
    .GetSection("FoundryLocal:AdditionalModels")
    .Get<string[]>() ?? Array.Empty<string>();

var internalPort = builder.Configuration.GetValue<int?>("FoundryLocal:InternalPort") ?? 55588;
var internalBaseUrl = $"http://127.0.0.1:{internalPort}";

var downloadEps = builder.Configuration.GetValue<bool?>("FoundryLocal:DownloadExecutionProviders") ?? true;

// WHY A UTILITY MODEL ID?
//   GitHub Copilot's AGENT surface uses TWO model slots simultaneously:
//     1. MAIN model   — the model the user selected in the picker (phi-4-mini).
//     2. UTILITY model — a lightweight background model for titles, commit
//                        messages, rename hints, etc.
//   When the main model is BYOK, VS Code needs a registered BYOK model for the
//   utility slot too. We advertise this synthetic alias from GET /v1/models and
//   remap it to defaultModelAlias on the POST path.
var utilityModelId = builder.Configuration["FoundryLocal:UtilityModelId"] ?? "copilot-utility-small";

// Register a named HttpClient for forwarding requests to the SDK's REST server.
// Long timeout: LLM responses (especially non-streaming) can exceed 100 seconds.
builder.Services.AddHttpClient("foundrylocal", client =>
{
    client.BaseAddress = new Uri(internalBaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});

// ---------------------------------------------------------------------------
// 2. BUILD THE APP
// ---------------------------------------------------------------------------
var app = builder.Build();
var logger = app.Logger;

// REQUEST MIDDLEWARE — logs each incoming request with timing.
// Shows method, path, caller, body snippet, and response time.
app.Use(async (ctx, next) =>
{
    ctx.Request.EnableBuffering();
    var sw = System.Diagnostics.Stopwatch.StartNew();

    // Capture a readable body snippet (POST only).
    string bodySnippet = "";
    if (ctx.Request.ContentLength > 0)
    {
        using var sr = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var raw = await sr.ReadToEndAsync();
        bodySnippet = raw.Length > 200 ? raw[..200] + "…" : raw;
        ctx.Request.Body.Position = 0;
    }

    // Show caller identity: prefer User-Agent app name, fall back to IP.
    var ua      = ctx.Request.Headers.UserAgent.ToString();
    var caller  = ua.Length > 0 ? ua.Split('/')[0].Trim() : ctx.Connection.RemoteIpAddress?.ToString() ?? "?";

    if (string.IsNullOrEmpty(bodySnippet))
        logger.LogInformation("→ {Method} {Path}  ({Caller})",
            ctx.Request.Method, ctx.Request.Path, caller);
    else
        logger.LogInformation("→ {Method} {Path}  ({Caller})  body: {Body}",
            ctx.Request.Method, ctx.Request.Path, caller, bodySnippet);

    await next();

    sw.Stop();
    logger.LogInformation("← {Method} {Path}  {Status}  {ElapsedMs}ms",
        ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
});

// ---------------------------------------------------------------------------
// 2b. INITIALISE THE FOUNDRY LOCAL SDK
//
//  The SDK handles the entire lifecycle:
//    • Starts the Foundry Local daemon process if it isn't already running.
//    • Downloads execution providers (GPU/NPU ONNX Runtime extensions)
//      so the model runs on the best available hardware.
//    • Downloads model weights on first use; reuses the local cache thereafter.
//    • Starts an internal OpenAI-compatible REST server at internalBaseUrl.
//
//  FoundryLocalManager is a SINGLETON — CreateAsync must be called ONCE.
// ---------------------------------------------------------------------------
logger.LogInformation("Initialising Foundry Local SDK (daemon + model: {Model})...", defaultModelAlias);

var sdkConfig = new Configuration
{
    AppName   = "FoundryLocalProxy",
    LogLevel  = Microsoft.AI.Foundry.Local.LogLevel.Warning, // keep noise low
    Web       = new Configuration.WebService { Urls = internalBaseUrl }
};

await FoundryLocalManager.CreateAsync(sdkConfig, logger);
var mgr = FoundryLocalManager.Instance;

// ---------------------------------------------------------------------------
// 2c. DOWNLOAD EXECUTION PROVIDERS (GPU / NPU drivers)
//
//  Execution providers are the ONNX Runtime extensions that give Foundry Local
//  access to hardware accelerators: CUDA (NVIDIA), QNN (Qualcomm NPU),
//  Vitis (AMD NPU), OpenVINO (Intel), TensorRT, etc.
//
//  WHY DOWNLOAD THEM?
//    Without the right EP, the model falls back to CPU, which is 3–10× slower.
//    The SDK detects the current hardware and only downloads what is relevant —
//    on a machine with no GPU the call completes in seconds.
//
//  The download is CACHED locally after the first run.
// ---------------------------------------------------------------------------
if (downloadEps)
{
    logger.LogInformation("Downloading execution providers for hardware acceleration...");
    var currentEp = "";
    await mgr.DownloadAndRegisterEpsAsync((epName, percent) =>
    {
        if (epName != currentEp)
        {
            if (currentEp != "") Console.WriteLine();
            currentEp = epName;
        }
        Console.Write($"\r  [EP] {epName,-35} {percent,5:F0}%");
    });
    if (currentEp != "") Console.WriteLine();
    logger.LogInformation("Execution providers ready.");
}

// ---------------------------------------------------------------------------
// 2d. DOWNLOAD AND LOAD MODELS
//
//  For each configured model (DefaultModel + AdditionalModels):
//    1. Look up the model by alias in the Foundry Local model catalog.
//    2. Call DownloadAsync() — this SKIPS the download if the model weights
//       are already in the local cache (~%USERPROFILE%/.foundry/cache).
//    3. Call LoadAsync() — loads the weights into memory for inference.
//
//  On first run the download may take several minutes (phi-4-mini ≈ 2.5 GB).
//  All subsequent starts are near-instant because the cache is reused.
//
//  The SDK automatically selects the best hardware-optimised model variant
//  (e.g., GPU-quantised vs CPU-quantised) based on the current device.
// ---------------------------------------------------------------------------
var catalog = await mgr.GetCatalogAsync();

// Collect all model aliases to load: DefaultModel first, then AdditionalModels.
var allAliases = new List<string> { defaultModelAlias };
allAliases.AddRange(additionalModels.Where(m => !string.IsNullOrWhiteSpace(m) && m != defaultModelAlias));

var loadedModels = new List<string>();        // canonical SDK ids
var loadedAliases = new List<string>();       // configured aliases (what VS Code uses)
// Maps alias → canonical id for the POST rewrite path.
var aliasToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

foreach (var alias in allAliases)
{
    var model = await catalog.GetModelAsync(alias);
    if (model is null)
    {
        logger.LogWarning("Model '{Alias}' not found in the Foundry Local catalog. Skipping. " +
                          "Run `foundry model list` to see available aliases.", alias);
        continue;
    }

    // DownloadAsync skips the download if the model is already cached locally.
    logger.LogInformation("Preparing model '{Alias}' (downloading if not cached)...", alias);
    var downloadingLabel = alias;
    await model.DownloadAsync(progress =>
    {
        Console.Write($"\r  [download] {downloadingLabel,-35} {progress,5:F0}%");
        if (progress >= 100f) Console.WriteLine();
    });

    logger.LogInformation("Loading model '{Alias}' into memory...", alias);
    await model.LoadAsync();
    loadedModels.Add(model.Id);          // e.g. "Phi-4-mini-instruct-cuda-gpu:5"
    loadedAliases.Add(alias);            // e.g. "phi-4-mini"
    aliasToCanonical[alias] = model.Id;  // alias → canonical
    logger.LogInformation("Model '{Alias}' ready (id: {Id}).", alias, model.Id);
}

// ---------------------------------------------------------------------------
// 2e. START THE SDK'S INTERNAL REST SERVER
//
//  The SDK starts an OpenAI-compatible REST server at internalBaseUrl.
//  Our proxy (on port 5101) forwards VS Code BYOK requests to this server.
//  The server stays up for the lifetime of the proxy process.
// ---------------------------------------------------------------------------
logger.LogInformation("Starting Foundry Local internal REST server on {Url}...", internalBaseUrl);
await mgr.StartWebServiceAsync();
logger.LogInformation("Internal REST server ready.");

// Include aliases in the set so the POST handler can pass them through unchanged.
var loadedModelSet = new HashSet<string>(loadedModels, StringComparer.OrdinalIgnoreCase);
loadedModelSet.UnionWith(loadedAliases); // add e.g. "phi-4-mini"
var defaultModel   = loadedAliases.FirstOrDefault() ?? loadedModels.FirstOrDefault() ?? defaultModelAlias;

logger.LogInformation(
    "Proxy ready. Loaded models: {Models}. Fallback: {Default}",
    loadedModels.Count > 0 ? string.Join(", ", loadedModels) : "(none)",
    defaultModel);

// ---------------------------------------------------------------------------
// 2f. GRACEFUL SHUTDOWN — stop the REST server when the proxy exits
//
//  WHY Task.Run + Wait instead of .GetAwaiter().GetResult()?
//    .GetAwaiter().GetResult() on a synchronous callback can deadlock if the
//    SDK awaits a context that is already blocked on this callback.
//    Task.Run offloads to a fresh thread pool thread so the async continuation
//    always has a free thread to complete on.  The 15-second cap ensures CTRL+C
//    always exits cleanly even if the Foundry Local daemon is slow to unload.
// ---------------------------------------------------------------------------
app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("Stopping Foundry Local internal REST server...");
    var stopped = Task.Run(() => mgr.StopWebServiceAsync()).Wait(TimeSpan.FromSeconds(15));
    if (!stopped)
        logger.LogWarning("Foundry Local REST server did not stop within 15 s — continuing shutdown.");
    else
        logger.LogInformation("Foundry Local REST server stopped.");
});

// ---------------------------------------------------------------------------
// 3. ENDPOINTS
// ---------------------------------------------------------------------------

// --- 3a. Health / info endpoint -------------------------------------------
app.MapGet("/", () => new
{
    status       = "ok",
    proxy        = "FoundryLocalProxy",
    model        = defaultModel,
    utilityModel = utilityModelId,
    loadedModels,
    internalRestServer = internalBaseUrl
});
app.MapGet("/health", () => new
{
    status       = "ok",
    proxy        = "FoundryLocalProxy",
    model        = defaultModel,
    utilityModel = utilityModelId,
    loadedModels,
    internalRestServer = internalBaseUrl
});

// --- 3b. Models list -------------------------------------------------------
// VS Code validates the configured model id against modelListUrl.
// We advertise the ALIASES (e.g. "phi-4-mini") so VS Code finds them by name,
// plus the utility alias.  Canonical SDK ids are included as well for completeness.
app.MapGet("/v1/models", () =>
{    var data = new List<object>();
    long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // Aliases first — these are the ids VS Code is configured with.
    foreach (var alias in loadedAliases)
        data.Add(new { id = alias, @object = "model", created = ts, owned_by = "foundry-local" });

    // Canonical SDK ids (for completeness / interop).
    foreach (var canonical in loadedModels)
    {
        if (!loadedAliases.Any(a => aliasToCanonical.TryGetValue(a, out var c) && c == canonical))
            data.Add(new { id = canonical, @object = "model", created = ts, owned_by = "foundry-local" });
    }

    // Utility alias — remapped to defaultModel by the POST handler.
    data.Add(new { id = utilityModelId, @object = "model", created = ts, owned_by = "foundry-local" });

    return Results.Json(new { @object = "list", data });
});

// --- 3b2. Known model catalog — all models the Foundry Local SDK can serve --
//
//  GET /v1/models/catalog  — ProxiesTestApp uses this to show the full catalog
//  with size, RAM requirements, and a description so users can decide which
//  models to add to FoundryLocal:AdditionalModels in appsettings.json.
//
//  "loaded" = currently running in memory (in loadedAliases)
//  "available" = in the Foundry Local public catalog but not loaded here
//
//  To add a model: set FoundryLocal:AdditionalModels: [ "phi-4", ... ] and restart.
app.MapGet("/v1/models/catalog", () =>
{
    var catalog = new[]
    {
        new { alias="phi-4-mini",         params_b=3.8,  size_gb=2.5, ram_gb=6,  description="Default. Fast, coding-focused, MIT license.",             status=loadedAliases.Contains("phi-4-mini",         StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
        new { alias="phi-4",              params_b=14.0, size_gb=9.0, ram_gb=16, description="Higher quality than phi-4-mini, needs more RAM.",           status=loadedAliases.Contains("phi-4",              StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
        new { alias="phi-3.5-mini",       params_b=3.8,  size_gb=2.4, ram_gb=6,  description="Previous Phi generation, similar size to phi-4-mini.",      status=loadedAliases.Contains("phi-3.5-mini",       StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
        new { alias="phi-3-mini",         params_b=3.8,  size_gb=2.2, ram_gb=6,  description="Older Phi, smallest in the Phi-3 family.",                  status=loadedAliases.Contains("phi-3-mini",         StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
        new { alias="llama-3.2-3b",       params_b=3.0,  size_gb=2.0, ram_gb=6,  description="Meta's fast 3B model, great for quick responses.",          status=loadedAliases.Contains("llama-3.2-3b",       StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
        new { alias="llama-3.2-1b",       params_b=1.0,  size_gb=1.0, ram_gb=4,  description="Smallest viable model, ultra-fast on CPU.",                 status=loadedAliases.Contains("llama-3.2-1b",       StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
        new { alias="mistral-7b-instruct",params_b=7.0,  size_gb=5.0, ram_gb=10, description="Strong coding and reasoning, needs ~8 GB RAM.",             status=loadedAliases.Contains("mistral-7b-instruct",StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
        new { alias="phi-3.5-moe",        params_b=42.0, size_gb=28.0,ram_gb=32, description="Mixture-of-Experts, best quality, needs high-end hardware.", status=loadedAliases.Contains("phi-3.5-moe",        StringComparer.OrdinalIgnoreCase) ? "loaded" : "available" },
    };
    return Results.Json(new { loaded = loadedAliases, catalog });
});

// --- 3c. Chat completions — THE CORE PROXY --------------------------------
//
//  Receives VS Code BYOK requests and forwards them to the SDK's internal
//  REST server.  Handles:
//    • Model pass-through: loaded model ids are forwarded unchanged.
//    • Utility alias rewrite: "copilot-utility-small" → defaultModel.
//    • Streaming: SSE bytes piped through in real time.
//    • [copilot ask] logging: unwraps the Copilot Chat XML envelope.
app.MapPost("/v1/chat/completions", async (HttpRequest request, HttpResponse response,
    IHttpClientFactory clientFactory, ILogger<Program> reqLogger) =>
{
    // STEP 1: Buffer the body (consumed once; possibly rewritten before forwarding).
    string bodyText;
    using (var reader = new StreamReader(request.Body))
        bodyText = await reader.ReadToEndAsync();

    // STEP 2: Parse, log the ask, detect streaming, rewrite model if needed.
    bool isStreaming     = false;
    string requestedModel = defaultModel;

    try
    {
        var jsonBody = JsonNode.Parse(bodyText) as JsonObject;
        if (jsonBody is not null)
        {
            // Log what the user actually typed (unwrap Copilot Chat XML envelope).
            var typedAsk = CopilotMessageExtractor.GetLastUserMessageText(jsonBody);
            reqLogger.LogInformation("[ask] {TypedAsk}", typedAsk);

            isStreaming    = jsonBody["stream"]?.GetValue<bool>() ?? false;
            requestedModel = jsonBody["model"]?.GetValue<string>() ?? defaultModel;

            // Resolve alias → canonical SDK id for the SDK's internal server.
            // The internal server only understands canonical ids.
            if (aliasToCanonical.TryGetValue(requestedModel, out var canonicalId))
            {
                reqLogger.LogDebug("[model] alias '{Alias}' → canonical '{Canonical}'", requestedModel, canonicalId);
                jsonBody["model"] = canonicalId;
                bodyText = jsonBody.ToJsonString();
            }
            else if (loadedModelSet.Contains(requestedModel))
            {
                // Already a canonical id — pass through unchanged.
                reqLogger.LogDebug("[model] passthrough '{Model}'", requestedModel);
            }
            else
            {
                // Unknown id (e.g. utility alias) — remap to first loaded canonical id.
                var fallbackCanonical = loadedModels.FirstOrDefault() ?? defaultModelAlias;
                reqLogger.LogInformation("[model] unknown '{Requested}' → fallback '{Fallback}'", requestedModel, fallbackCanonical);
                jsonBody["model"] = fallbackCanonical;
                bodyText = jsonBody.ToJsonString();
            }
        }
    }
    catch (JsonException) { /* malformed JSON — let the SDK return the error */ }

    // LLM activity span — shows in the Aspire Traces tab as "llm.chat" with
    // tags: llm.proxy=FoundryLocalProxy, llm.model, llm.streaming, llm.latency_ms.
    var llmSw   = System.Diagnostics.Stopwatch.StartNew();
    using var llmSpan = LlmActivity.StartChat("FoundryLocalProxy", requestedModel, isStreaming);

    // STEP 3: Forward to the SDK's internal REST server.
    var httpClient = clientFactory.CreateClient("foundrylocal");

    using var fwdRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
    {
        Content = new StringContent(bodyText, System.Text.Encoding.UTF8, "application/json")
    };

    var completionOption = isStreaming
        ? HttpCompletionOption.ResponseHeadersRead
        : HttpCompletionOption.ResponseContentRead;

    HttpResponseMessage fwdResponse;
    try
    {
        fwdResponse = await httpClient.SendAsync(fwdRequest, completionOption);
    }
    catch (TaskCanceledException ex) when (ex.CancellationToken == request.HttpContext.RequestAborted)
    {
        // Client disconnected or server shutting down — nothing to write back.
        LlmActivity.SetResult(llmSpan, llmSw.ElapsedMilliseconds, error: "client disconnected");
        return;
    }
    catch (TaskCanceledException)
    {
        LlmActivity.SetResult(llmSpan, llmSw.ElapsedMilliseconds, error: "timeout");
        response.StatusCode = 504;
        await response.WriteAsync("{\"error\":\"Foundry Local request timed out after 5 minutes.\"}");
        return;
    }
    catch (HttpRequestException ex)
    {
        LlmActivity.SetResult(llmSpan, llmSw.ElapsedMilliseconds, error: ex.Message);
        response.StatusCode = 502;
        await response.WriteAsync($"{{\"error\":\"Could not reach Foundry Local internal server at {internalBaseUrl}: {ex.Message}\"}}");
        return;
    }

    // STEP 4: Relay the response — rewrite SSE chunks or return full JSON.
    response.StatusCode = (int)fwdResponse.StatusCode;

    if (isStreaming)
    {
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"]     = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        // The Foundry Local SDK emits non-standard fields in every chunk:
        //   • model:         canonical id (e.g. "Phi-4-mini-instruct-cuda-gpu:5") — VS Code
        //                    validates this must match the requested id ("phi-4-mini")
        //   • IsDelta, Successful, HttpStatusCode, CreatedAt — non-standard extras
        //   • choices[].message alongside choices[].delta — non-standard
        //   • choices[].delta.tool_calls:[] — VS Code treats this as a tool call event
        //   • duplicate data:[DONE] — SDK sometimes sends [DONE] twice, confusing VS Code
        //
        // We parse each SSE line and emit a clean standard OpenAI chunk instead.
        await using var sdkStream = await fwdResponse.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(sdkStream);
        bool doneSent = false;
        string? line;
        var enc = System.Text.Encoding.UTF8;
        // Use the request's abort token so the loop exits immediately when:
        //   • the client disconnects, OR
        //   • CTRL+C is pressed (ApplicationStopping cancels all active requests).
        var ct = request.HttpContext.RequestAborted;

        while (!ct.IsCancellationRequested &&
               (line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (line.StartsWith("data: [DONE]", StringComparison.Ordinal))
            {
                if (!doneSent)
                {
                    await response.Body.WriteAsync(enc.GetBytes("data: [DONE]\n\n"));
                    await response.Body.FlushAsync();
                    doneSent = true;
                }
                continue; // skip duplicate [DONE]
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal) || doneSent)
                continue; // skip blank lines, comments, and anything after [DONE]

            var json = line[6..]; // strip "data: "
            try
            {
                var node = JsonNode.Parse(json) as JsonObject;
                if (node is null) continue;

                // Fix model: use the alias the client requested, not the canonical SDK id.
                node["model"] = requestedModel;

                // Clean each choice: remove non-standard fields.
                if (node["choices"] is JsonArray choices)
                {
                    foreach (var choice in choices)
                    {
                        if (choice is not JsonObject c) continue;
                        c.Remove("message");      // non-standard; only "delta" belongs here
                        if (c["delta"] is JsonObject delta)
                        {
                            // Remove empty tool_calls array — VS Code treats [] as a tool event.
                            if (delta["tool_calls"] is JsonArray tc && tc.Count == 0)
                                delta.Remove("tool_calls");
                        }
                    }
                }

                // Remove non-standard top-level fields.
                node.Remove("IsDelta");
                node.Remove("Successful");
                node.Remove("HttpStatusCode");
                node.Remove("CreatedAt");

                // Ensure required fields are present.
                if (node["object"] is null) node["object"] = "chat.completion.chunk";

                // Skip SDK artifact: empty choices chunks carry no useful data.
                if (node["choices"] is JsonArray ch && ch.Count == 0) continue;

                var outLine = $"data: {node.ToJsonString()}\n\n";
                await response.Body.WriteAsync(enc.GetBytes(outLine));
                await response.Body.FlushAsync();
            }
            catch (JsonException)
            {
                // Unparseable line — forward as-is (safe fallback).
                await response.Body.WriteAsync(enc.GetBytes($"{line}\n\n"));
                await response.Body.FlushAsync();
            }
        }

        if (!doneSent)
        {
            await response.Body.WriteAsync(enc.GetBytes("data: [DONE]\n\n"));
            await response.Body.FlushAsync();
        }
    }
    else
    {
        response.ContentType = "application/json; charset=utf-8";
        var rawJson = await fwdResponse.Content.ReadAsStringAsync();
        // Fix model name in non-streaming response too.
        try
        {
            var node = JsonNode.Parse(rawJson) as JsonObject;
            if (node is not null) { node["model"] = requestedModel; rawJson = node.ToJsonString(); }
        }
        catch (JsonException) { /* leave as-is */ }
        await response.WriteAsync(rawJson);
    }

    llmSw.Stop();
    LlmActivity.SetResult(llmSpan, llmSw.ElapsedMilliseconds);
});

// ---------------------------------------------------------------------------
// 4. RUN — port is configured via launchSettings.json (standalone dotnet run)
//          or ASPNETCORE_URLS from the Aspire AppHost (proxies/AppHost).
//          Both pin to 5101 so VS Code BYOK settings never need to change.
// ---------------------------------------------------------------------------
app.Run();
