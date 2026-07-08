using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Telemetry;

/// <summary>
/// Token usage captured from an upstream chat completion response, following the
/// OpenAI usage shape (<c>prompt_tokens</c> / <c>completion_tokens</c> / <c>total_tokens</c>).
/// </summary>
public sealed record TokenUsage(long? InputTokens, long? OutputTokens, long? TotalTokens, string? ResponseModel);

/// <summary>
/// GenAI OpenTelemetry instrumentation for the router. Emits a client span per upstream
/// chat-completion call using the OpenTelemetry GenAI semantic conventions, plus a token-usage
/// histogram, so the full request flow and token counts show up in the Aspire dashboard.
/// </summary>
public static class GenAiTelemetry
{
    /// <summary>ActivitySource name. Registered with the tracer provider in Program.cs.</summary>
    public const string ActivitySourceName = "ElBruno.CopilotHarness.GenAI";

    /// <summary>Meter name. Registered with the meter provider in Program.cs.</summary>
    public const string MeterName = "ElBruno.CopilotHarness.GenAI";

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    // OTEL GenAI semantic-convention metric for token usage, dimensioned by gen_ai.token.type.
    private static readonly Histogram<long> TokenUsageHistogram = Meter.CreateHistogram<long>(
        "gen_ai.client.token.usage",
        unit: "token",
        description: "Number of input and output tokens used per GenAI chat completion.");

    /// <summary>Maps a provider type to its OTEL <c>gen_ai.system</c> value.</summary>
    public static string SystemFor(ModelProviderType type) => type switch
    {
        ModelProviderType.Ollama => "ollama",
        ModelProviderType.AzureOpenAI => "azure.ai.openai",
        ModelProviderType.FoundryLocal => "microsoft.foundry.local",
        _ => "openai"
    };

    /// <summary>
    /// Starts a GenAI <c>chat</c> client span for the resolved model. Returns <c>null</c> when no
    /// listener is attached (e.g. OTLP export disabled), so callers pay nothing outside Aspire.
    /// </summary>
    public static Activity? StartChatSpan(
        ModelProfileOptions model,
        string profileName,
        string traceId,
        bool stream,
        bool hadTools,
        bool toolOverrideApplied)
    {
        var system = SystemFor(model.Type);
        var activity = Source.StartActivity($"chat {model.Deployment}", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("gen_ai.operation.name", "chat");
        activity.SetTag("gen_ai.system", system);
        activity.SetTag("gen_ai.request.model", model.Deployment);
        activity.SetTag("harness.routing.profile", profileName);
        activity.SetTag("harness.trace_id", traceId);
        activity.SetTag("harness.stream", stream);
        activity.SetTag("harness.had_tools", hadTools);
        activity.SetTag("harness.tool_override", toolOverrideApplied);
        return activity;
    }

    /// <summary>
    /// Records token usage on the span and the histogram using GenAI semantic conventions.
    /// Safe to call with a <c>null</c> span; a <c>null</c> usage is ignored.
    /// </summary>
    public static void RecordUsage(Activity? activity, TokenUsage? usage, string system, string requestModel)
    {
        if (usage is null)
        {
            return;
        }

        var responseModel = string.IsNullOrWhiteSpace(usage.ResponseModel) ? requestModel : usage.ResponseModel!;

        if (activity is not null)
        {
            activity.SetTag("gen_ai.response.model", responseModel);
            if (usage.InputTokens is long input)
            {
                activity.SetTag("gen_ai.usage.input_tokens", input);
            }

            if (usage.OutputTokens is long output)
            {
                activity.SetTag("gen_ai.usage.output_tokens", output);
            }
        }

        if (usage.InputTokens is long inputTokens)
        {
            TokenUsageHistogram.Record(
                inputTokens,
                new KeyValuePair<string, object?>("gen_ai.system", system),
                new KeyValuePair<string, object?>("gen_ai.token.type", "input"),
                new KeyValuePair<string, object?>("gen_ai.response.model", responseModel));
        }

        if (usage.OutputTokens is long outputTokens)
        {
            TokenUsageHistogram.Record(
                outputTokens,
                new KeyValuePair<string, object?>("gen_ai.system", system),
                new KeyValuePair<string, object?>("gen_ai.token.type", "output"),
                new KeyValuePair<string, object?>("gen_ai.response.model", responseModel));
        }
    }

    /// <summary>Marks the span as failed when the upstream call throws.</summary>
    public static void RecordError(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    internal static string ToInvariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
