using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Logging;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Encapsulates the tool-calling capability guard and the local-route output-token clamp that
/// run just before an outbound <c>/v1/chat/completions</c> request is dispatched.
/// <list type="bullet">
///   <item>A tool request that lands on a model which can't do tool-calling is redirected to a
///   tool-capable model (small payloads prefer the local tool-caller; heavy ones prefer the cloud).</item>
///   <item>A tool request that lands on the LOCAL model with a heavy agentic payload is redirected to
///   the cloud too — even though the local model is tool-capable, it can't serve a huge working set
///   without over-generating and tripping the client's "Response too long" cap.</item>
///   <item>As a safety net, LOCAL (Ollama) routes have their output token count clamped so a small
///   local model cannot run away and produce an oversized response.</item>
/// </list>
/// </summary>
public static class ToolRoutingGuard
{
    /// <summary>
    /// The outcome of applying the guard: the (possibly overridden) target profile plus a
    /// human-readable override reason when a redirect occurred.
    /// </summary>
    public readonly record struct GuardResult(
        string ProfileName,
        ModelProfileOptions Profile,
        string? OverrideReason,
        bool OverrideApplied,
        bool HadTools);

    /// <summary>
    /// Applies the tool-capability override and the local-route token clamp to
    /// <paramref name="requestPayload"/> in place, returning the resolved target profile.
    /// </summary>
    public static GuardResult Apply(
        JsonObject requestPayload,
        string selectedProfileName,
        ModelProfileOptions selectedProfile,
        RoutingOptions routingOptions,
        string traceId,
        ILogger logger)
    {
        var requestHadTools = OpenAiApiUtilities.RequestHasTools(requestPayload);
        var overrideApplied = false;
        string? overrideReason = null;

        if (requestHadTools)
        {
            var totalPromptChars = BasicModelRouter.GetPromptCharacterCount(requestPayload);
            var localMax = routingOptions.Rules.LocalToolCallingMaxPromptCharacters;
            var payloadWithinLocalLimit = localMax <= 0 || totalPromptChars <= localMax;
            var selectedIsLocal = selectedProfile.Type == ModelProviderType.Ollama;

            var modelCannotDoTools = !selectedProfile.SupportsToolCalling;
            var localPayloadTooLarge = selectedIsLocal && !payloadWithinLocalLimit;

            if (modelCannotDoTools || localPayloadTooLarge)
            {
                // Small tool requests prefer the local tool-caller; oversized agentic payloads prefer the cloud.
                var preferLocal = payloadWithinLocalLimit;
                var capable = OpenAiApiUtilities.FindToolCapableModel(routingOptions, selectedProfileName, preferLocal);
                if (capable is { } target)
                {
                    overrideReason = localPayloadTooLarge
                        ? $"Agentic tool request payload ({totalPromptChars} chars) exceeds the local limit ({localMax}); routed from local '{selectedProfileName}' to cloud '{target.ProfileName}' to avoid an oversized response."
                        : payloadWithinLocalLimit
                            ? $"Request requires tool-calling; '{selectedProfileName}' does not support it, routed to '{target.ProfileName}'."
                            : $"Request requires tool-calling; '{selectedProfileName}' does not support it, routed to '{target.ProfileName}' (payload {totalPromptChars} chars exceeds local limit {localMax}, preferring cloud).";
                    selectedProfileName = target.ProfileName;
                    selectedProfile = target.Profile;
                    requestPayload["model"] = selectedProfile.Deployment;
                    overrideApplied = true;
                    logger.LogInformation(
                        "Tool-capability override applied for trace {TraceId}. {OverrideReason}",
                        traceId,
                        overrideReason);
                }
                else
                {
                    overrideReason =
                        $"Request requires tool-calling but '{selectedProfileName}' cannot serve it and no alternative tool-capable model is enabled.";
                    logger.LogWarning(
                        "Tool-capability override unavailable for trace {TraceId}. {OverrideReason}",
                        traceId,
                        overrideReason);
                }
            }
        }

        // Safety net: cap output tokens for LOCAL (Ollama) routes so a small local model cannot run away
        // and produce an oversized response (which the client rejects as "Response too long").
        if (selectedProfile.Type == ModelProviderType.Ollama &&
            routingOptions.Rules.LocalRouteMaxTokens > 0)
        {
            OpenAiApiUtilities.ClampMaxTokens(requestPayload, routingOptions.Rules.LocalRouteMaxTokens);
        }

        return new GuardResult(selectedProfileName, selectedProfile, overrideReason, overrideApplied, requestHadTools);
    }
}
