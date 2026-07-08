using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Fires a silent parallel request to a shadow model profile immediately after
/// the primary request is routed. Results are stored for comparison but never
/// shown to the client.
/// </summary>
public interface IShadowRoutingService
{
    /// <summary>
    /// Fire-and-forget a shadow request. Returns immediately; does not await the shadow call.
    /// </summary>
    void FireAndForget(
        JsonObject requestBody,
        string primaryProfile,
        string primaryTraceId,
        double primaryLatencyMs,
        int primaryStatusCode,
        CancellationToken cancellationToken = default);
}

public sealed class ShadowRoutingService(
    FoundryChatCompletionsClient foundryClient,
    IShadowRoutingStore shadowStore,
    IRuleConfidenceStore confidenceStore,
    IRoutingConfigurationStore configStore,
    ILogger<ShadowRoutingService> logger) : IShadowRoutingService
{
    private readonly FoundryChatCompletionsClient _foundryClient = foundryClient;
    private readonly IShadowRoutingStore _shadowStore = shadowStore;
    private readonly IRuleConfidenceStore _confidenceStore = confidenceStore;
    private readonly IRoutingConfigurationStore _configStore = configStore;
    private readonly ILogger<ShadowRoutingService> _logger = logger;

    public void FireAndForget(
        JsonObject requestBody,
        string primaryProfile,
        string primaryTraceId,
        double primaryLatencyMs,
        int primaryStatusCode,
        CancellationToken cancellationToken = default)
    {
        _ = RunShadowAsync(requestBody, primaryProfile, primaryTraceId, primaryLatencyMs, primaryStatusCode);
    }

    private async Task RunShadowAsync(
        JsonObject requestBody,
        string primaryProfile,
        string primaryTraceId,
        double primaryLatencyMs,
        int primaryStatusCode)
    {
        try
        {
            var config = await _shadowStore.GetConfigAsync(CancellationToken.None);
            if (!config.Enabled) return;

            if (Random.Shared.NextDouble() > config.SamplingRate) return;

            var routingOptions = await _configStore.GetRoutingOptionsAsync(CancellationToken.None);
            if (!routingOptions.TryGetProfile(config.ShadowProfile, out var shadowProfileOptions))
            {
                _logger.LogDebug(
                    "Shadow routing skipped: profile '{ShadowProfile}' not found.",
                    config.ShadowProfile);
                return;
            }

            var promptHash = ComputePromptHash(requestBody);
            var shadowId = await _shadowStore.RecordShadowRequestAsync(
                primaryTraceId, primaryProfile, config.ShadowProfile, promptHash,
                CancellationToken.None);

            var shadowPayload = System.Text.Json.Nodes.JsonNode.Parse(requestBody.ToJsonString())!.AsObject();
            shadowPayload["model"] = shadowProfileOptions.Deployment;
            shadowPayload["stream"] = false;

            var started = DateTimeOffset.UtcNow;
            int shadowStatus;
            double shadowLatencyMs;

            try
            {
                using var response = await _foundryClient.SendChatCompletionsAsync(
                    shadowPayload,
                    shadowProfileOptions,
                    stream: false,
                    CancellationToken.None);

                shadowLatencyMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                shadowStatus = (int)response.StatusCode;
            }
            catch (Exception ex)
            {
                shadowLatencyMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                shadowStatus = 500;
                _logger.LogWarning(ex, "Shadow routing call failed for shadow id {ShadowId}.", shadowId);
            }

            var outcomeLabel = primaryStatusCode < 400 && shadowStatus < 400 ? "both-success"
                : primaryStatusCode < 400 ? "primary-only"
                : shadowStatus < 400 ? "shadow-only"
                : "both-failed";

            await _shadowStore.UpdateShadowOutcomeAsync(
                shadowId,
                primaryLatencyMs,
                shadowLatencyMs,
                primaryStatusCode,
                shadowStatus,
                outcomeLabel,
                CancellationToken.None);

            // Track rule confidence: when both succeed, the routing rule is validated
            var ruleKey = $"profile:{primaryProfile}";
            await _confidenceStore.RecordInvocationAsync(
                ruleKey,
                successful: primaryStatusCode < 400,
                CancellationToken.None);

            _logger.LogInformation(
                "Shadow routing completed: primary={PrimaryProfile} shadow={ShadowProfile} outcome={Outcome}",
                primaryProfile,
                config.ShadowProfile,
                outcomeLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shadow routing background task failed for trace {TraceId}.", primaryTraceId);
        }
    }

    private static string ComputePromptHash(JsonObject requestBody)
    {
        var messages = requestBody["messages"]?.ToJsonString() ?? "";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(messages));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
