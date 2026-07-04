using System.Diagnostics;

namespace Proxies.ServiceDefaults;

// =============================================================================
//  LlmActivity — OpenTelemetry ActivitySource for LLM proxy calls.
//
//  Follows the OpenTelemetry GenAI semantic conventions so the Aspire dashboard
//  renders a proper "GenAI" trace view alongside the raw HTTP spans.
//
//  Required gen_ai.* attributes (per OTel GenAI spec 1.26+):
//    gen_ai.system           — AI provider ("foundry_local", "ollama", "azure_openai")
//    gen_ai.operation.name   — always "chat" for chat-completions
//    gen_ai.request.model    — the model id/alias requested by the client
//
//  Optional gen_ai.* attributes added when known:
//    gen_ai.response.model   — canonical model id returned by the backend
//    gen_ai.usage.input_tokens  / gen_ai.usage.output_tokens
//
//  Span name follows the convention: "{operation} {model}"  e.g. "chat phi-4-mini"
// =============================================================================
public static class LlmActivity
{
    /// <summary>ActivitySource name — registered in Extensions.cs so Aspire captures it.</summary>
    public const string SourceName = "Proxies.LlmActivity";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    // Maps proxy name → gen_ai.system value (OTel GenAI semantic convention).
    static string ToGenAiSystem(string proxy) => proxy switch
    {
        "FoundryLocalProxy" => "foundry_local",
        "FoundryProxy"      => "azure_openai",
        "OllamaProxy"       => "ollama",
        _                   => proxy.ToLowerInvariant()
    };

    /// <summary>
    /// Starts a new LLM chat span using the OTel GenAI semantic conventions.
    /// Span name: "chat {model}" (e.g. "chat phi-4-mini").
    /// </summary>
    public static Activity? StartChat(string proxy, string model, bool streaming)
    {
        var activity = Source.StartActivity($"chat {model}", ActivityKind.Client);
        if (activity is null) return null;

        // --- OTel GenAI semantic conventions (required) ---
        activity.SetTag("gen_ai.system",         ToGenAiSystem(proxy));
        activity.SetTag("gen_ai.operation.name", "chat");
        activity.SetTag("gen_ai.request.model",  model);

        // --- Extra proxy context (displayed in Aspire span detail) ---
        activity.SetTag("llm.proxy",     proxy);
        activity.SetTag("llm.streaming", streaming);

        return activity;
    }

    /// <summary>
    /// Records result tags on the span.  Always call before the span goes out of scope.
    /// </summary>
    public static void SetResult(Activity? activity, long latencyMs,
        int?    inputTokens  = null,
        int?    outputTokens = null,
        string? responseModel = null,
        string? error = null)
    {
        if (activity is null) return;

        // --- OTel GenAI semantic conventions (optional, set when known) ---
        if (inputTokens.HasValue)
            activity.SetTag("gen_ai.usage.input_tokens",  inputTokens.Value);
        if (outputTokens.HasValue)
            activity.SetTag("gen_ai.usage.output_tokens", outputTokens.Value);
        if (responseModel is not null)
            activity.SetTag("gen_ai.response.model", responseModel);

        // --- Extra latency tag ---
        activity.SetTag("llm.latency_ms", latencyMs);

        if (error is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error);
            activity.SetTag("error.message", error);
        }
        else
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }
}
