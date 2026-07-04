using System.Diagnostics;

namespace Proxies.ServiceDefaults;

// =============================================================================
//  LlmActivity — shared OpenTelemetry ActivitySource for LLM proxy calls.
//
//  USAGE (in each proxy's POST /v1/chat/completions handler):
//
//    var sw = Stopwatch.StartNew();
//    using var span = LlmActivity.StartChat("OllamaProxy", requestedModel, isStreaming);
//    try
//    {
//        // ... forward to backend ...
//        sw.Stop();
//        LlmActivity.SetResult(span, sw.ElapsedMilliseconds);
//    }
//    catch (Exception ex)
//    {
//        LlmActivity.SetResult(span, sw.ElapsedMilliseconds, error: ex.Message);
//        throw;
//    }
//
//  ASPIRE DASHBOARD:
//    Each span appears in Traces → "llm.chat" with tags that make proxy
//    comparison and debugging easy:
//      llm.proxy      — which proxy handled it (OllamaProxy / FoundryProxy / ...)
//      llm.model      — model alias that was requested (phi-4-mini, gpt-5-mini ...)
//      llm.streaming  — true / false
//      llm.latency_ms — full round-trip time including Foundry / Ollama inference
// =============================================================================
public static class LlmActivity
{
    /// <summary>ActivitySource name — registered in Extensions.cs so Aspire captures it.</summary>
    public const string SourceName = "Proxies.LlmActivity";

    // One shared source for all proxies; the "llm.proxy" tag distinguishes them.
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    /// <summary>Starts a new LLM chat span tagged with proxy + model + streaming flag.</summary>
    public static Activity? StartChat(string proxy, string model, bool streaming)
    {
        var activity = Source.StartActivity("llm.chat", ActivityKind.Server);
        if (activity is null) return null;

        activity.SetTag("llm.proxy",     proxy);
        activity.SetTag("llm.model",     model);
        activity.SetTag("llm.streaming", streaming);
        return activity;
    }

    /// <summary>
    /// Records result tags on the span.
    /// Always call this before the <c>using var span</c> goes out of scope so
    /// the Aspire trace shows accurate timing and success/error status.
    /// </summary>
    public static void SetResult(Activity? activity, long latencyMs,
        int? tokensEstimate = null, string? error = null)
    {
        if (activity is null) return;

        activity.SetTag("llm.latency_ms", latencyMs);

        if (tokensEstimate.HasValue)
            activity.SetTag("llm.tokens_estimate", tokensEstimate.Value);

        if (error is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error);
            activity.SetTag("llm.error", error);
        }
        else
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }
}
