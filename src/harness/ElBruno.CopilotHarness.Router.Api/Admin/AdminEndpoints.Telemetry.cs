using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapTelemetryEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/telemetry/clients", (IExecutionTraceStore traceStore, int? limit) =>
        {
            var normalizedLimit = Math.Clamp(limit ?? 200, 1, 500);
            var traces = traceStore.GetRecent(normalizedLimit);
            var now = DateTimeOffset.UtcNow;

            var clients = traces
                .GroupBy(trace => GetContextValue(trace, "request.client.id") ?? "unknown", StringComparer.OrdinalIgnoreCase)
                .Select(grouped =>
                {
                    var ordered = grouped.OrderByDescending(item => item.CreatedAtUtc).ToList();
                    var latest = ordered[0];
                    var clientId = grouped.Key;

                    return new ConnectedClientTelemetryDto(
                        ClientId: clientId,
                        DisplayName: GetClientDisplayName(clientId),
                        Source: GetContextValue(latest, "request.client.source") ?? "unknown",
                        Version: GetContextValue(latest, "request.client.version"),
                        LastSeenUtc: latest.CreatedAtUtc,
                        RequestsLastHour: grouped.Count(item => item.CreatedAtUtc >= now.AddHours(-1)),
                        LastProfile: latest.Decision.ProfileName,
                        LastDeployment: latest.Decision.Profile.Deployment);
                })
                .OrderByDescending(client => client.LastSeenUtc)
                .ToList();

            return Results.Ok(new ConnectedClientsResponse(now, clients));
        });

        group.MapGet("/telemetry/requests", (IExecutionTraceStore traceStore, int? limit) =>
        {
            var normalizedLimit = Math.Clamp(limit ?? 200, 1, 200);
            var traces = traceStore.GetRecent(normalizedLimit);

            var requests = traces
                .OrderByDescending(trace => trace.CreatedAtUtc)
                .Take(normalizedLimit)
                .Select(trace =>
                {
                    var clientId = GetContextValue(trace, "request.client.id") ?? "unknown";
                    return new LiveRequestTelemetryDto(
                        TraceId: trace.TraceId,
                        CreatedAtUtc: trace.CreatedAtUtc,
                        Endpoint: GetContextValue(trace, "request.endpoint") ?? "unknown",
                        ClientId: clientId,
                        ClientDisplayName: GetClientDisplayName(clientId),
                        ClientVersion: GetContextValue(trace, "request.client.version"),
                        Profile: trace.Decision.ProfileName,
                        Deployment: trace.Decision.Profile.Deployment,
                        Reason: trace.Decision.Reason,
                        ClassificationIntent: trace.Classification.Intent,
                        ClassificationComplexity: trace.Classification.Complexity);
                })
                .ToList();

            return Results.Ok(new LiveRequestsResponse(DateTimeOffset.UtcNow, requests));
        });

        group.MapGet("/telemetry/feed", (
            IExecutionTraceStore traceStore,
            IOptions<TelemetryOptions> telemetryOptions,
            int? limit) =>
        {
            var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
            var traces = traceStore.GetRecent(normalizedLimit);

            var requests = traces
                .OrderByDescending(trace => trace.CreatedAtUtc)
                .Take(normalizedLimit)
                .Select(trace =>
                {
                    var clientId = GetContextValue(trace, "request.client.id") ?? "unknown";
                    var semanticRule = GetContextValue(trace, "semantic.matchedRule");
                    var matchedRule = !string.IsNullOrWhiteSpace(semanticRule)
                        ? semanticRule
                        : ExtractMatchedRuleName(trace.Decision.Reason);
                    var semanticReason = GetContextValue(trace, "semantic.reason");
                    var rawUserMessage = GetContextValue(trace, "request.rawUserMessage");
                    int.TryParse(GetContextValue(trace, "request.promptCharacters"), out var promptChars);
                    int.TryParse(GetContextValue(trace, "request.totalPromptCharacters"), out var totalPromptChars);
                    var hasSystemMessage = string.Equals(GetContextValue(trace, "request.hasSystemMessage"), "true", StringComparison.OrdinalIgnoreCase);
                    var classifierSource = GetContextValue(trace, "classifier.source")
                        ?? (string.IsNullOrWhiteSpace(trace.Classification.Source) ? "deterministic" : trace.Classification.Source);
                    var processorModel = GetContextValue(trace, "classifier.processorModel") ?? trace.Classification.ProcessorModel;
                    var processorModelType = GetContextValue(trace, "classifier.processorModelType");
                    var userAgent = GetContextValue(trace, "request.client.userAgent");
                    int? upstreamStatusCode = int.TryParse(GetContextValue(trace, "upstream.status"), out var statusCode) ? statusCode : null;
                    double? upstreamLatencyMs = double.TryParse(
                        GetContextValue(trace, "upstream.latencyMs"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var latency) ? latency : null;
                    var upstreamSucceededFact = GetContextValue(trace, "upstream.succeeded");
                    var upstreamSucceeded = upstreamSucceededFact is null
                        || string.Equals(upstreamSucceededFact, "true", StringComparison.OrdinalIgnoreCase);
                    var upstreamError = GetContextValue(trace, "upstream.error");
                    var requestHadTools = string.Equals(GetContextValue(trace, "request.hadTools"), "true", StringComparison.OrdinalIgnoreCase);
                    var toolOverrideApplied = string.Equals(GetContextValue(trace, "routing.toolOverride"), "true", StringComparison.OrdinalIgnoreCase);
                    var overrideReason = GetContextValue(trace, "routing.toolOverrideReason");
                    long? tokensIn = long.TryParse(GetContextValue(trace, "gen_ai.usage.input_tokens"), out var tin) ? tin : null;
                    long? tokensOut = long.TryParse(GetContextValue(trace, "gen_ai.usage.output_tokens"), out var tout) ? tout : null;
                    long? tokensTotal = long.TryParse(GetContextValue(trace, "gen_ai.usage.total_tokens"), out var ttot) ? ttot : null;
                    var responseModel = GetContextValue(trace, "gen_ai.response.model");
                    // Shadow processor A/B facts.
                    var shadowIntent = GetContextValue(trace, "shadow.intent");
                    var shadowProcessorModel = GetContextValue(trace, "shadow.processorModel");
                    bool? shadowAgreement = bool.TryParse(GetContextValue(trace, "shadow.agreement"), out var agree) ? agree : null;
                    return new RoutedRequestView(
                        TraceId: trace.TraceId,
                        CreatedAtUtc: trace.CreatedAtUtc,
                        ClientId: clientId,
                        ClientDisplayName: GetClientDisplayName(clientId, userAgent),
                        Endpoint: GetContextValue(trace, "request.endpoint") ?? "unknown",
                        Stream: string.Equals(GetContextValue(trace, "request.stream"), "true", StringComparison.OrdinalIgnoreCase),
                        RequestedModel: GetContextValue(trace, "request.requestedModel"),
                        SelectedModel: trace.Decision.ProfileName,
                        Deployment: trace.Decision.Profile.Deployment,
                        MatchedRuleName: matchedRule,
                        Reason: trace.Decision.Reason,
                        Explanation: BuildExplanation(trace, matchedRule, classifierSource, processorModel),
                        PromptPreview: GetContextValue(trace, PromptPrivacy.PromptPreviewFactKey),
                        PromptCharacters: promptChars,
                        ClassificationIntent: trace.Classification.Intent,
                        ClassificationComplexity: trace.Classification.Complexity,
                        ClassifierSource: classifierSource,
                        ProcessorModel: processorModel,
                        ProcessorModelType: processorModelType,
                        ClassificationConfidence: trace.Classification.Confidence,
                        TotalPromptCharacters: totalPromptChars,
                        HasSystemMessage: hasSystemMessage,
                        RawUserMessage: rawUserMessage,
                        SemanticReason: semanticReason,
                        UpstreamStatusCode: upstreamStatusCode,
                        UpstreamLatencyMs: upstreamLatencyMs,
                        UpstreamSucceeded: upstreamSucceeded,
                        UpstreamError: upstreamError,
                        RequestHadTools: requestHadTools,
                        ToolCapabilityOverrideApplied: toolOverrideApplied,
                        OverrideReason: overrideReason,
                        TokensIn: tokensIn,
                        TokensOut: tokensOut,
                        TokensTotal: tokensTotal,
                        ResponseModel: responseModel,
                        ShadowIntent: shadowIntent,
                        ShadowProcessorModel: shadowProcessorModel,
                        ShadowAgreement: shadowAgreement);
                })
                .ToList();

            return Results.Ok(new RoutingFeedResponse(
                DateTimeOffset.UtcNow,
                telemetryOptions.Value.CapturePromptText,
                requests));
        });
    }
}
