using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static ModelConnectionDto ToModelConnectionDto(ModelConnectionRecord model) =>
        new(
            model.Id,
            model.Name,
            ProviderTypeToString(model.ProviderType),
            model.Endpoint,
            model.ModelName,
            model.ApiVersion,
            model.HasApiKey,
            model.Enabled,
            model.IsProcessor,
            model.SupportsCustomTemperature,
            model.SupportsToolCalling,
            model.UpdatedAtUtc);

    private static UpsertModelConnectionRequest ToUpsertModelConnectionRequest(ModelConnectionUpsertRequest request) =>
        new(
            request.Name.Trim(),
            ParseProviderType(request.Type),
            request.Endpoint?.Trim() ?? string.Empty,
            request.ModelName?.Trim() ?? string.Empty,
            request.ApiVersion?.Trim() ?? string.Empty,
            request.ApiKey,
            request.Enabled,
            request.IsProcessor,
            request.SupportsCustomTemperature,
            request.SupportsToolCalling);

    private static RoutingRuleDto ToRoutingRuleDto(RoutingRuleRecord rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.Description,
            rule.ConditionType.ToString(),
            rule.ConditionValue,
            rule.TargetModel,
            rule.Priority,
            rule.Enabled,
            rule.UpdatedAtUtc);

    private static string ProviderTypeToString(ModelProviderType type) =>
        type switch
        {
            ModelProviderType.Ollama => "ollama",
            ModelProviderType.AzureOpenAI => "azure-openai",
            _ => type.ToString().ToLowerInvariant()
        };

    private static ModelProviderType ParseProviderType(string? type) =>
        (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ollama" => ModelProviderType.Ollama,
            "azure-openai" => ModelProviderType.AzureOpenAI,
            "azureopenai" => ModelProviderType.AzureOpenAI,
            "azure" => ModelProviderType.AzureOpenAI,
            "foundry" => ModelProviderType.AzureOpenAI,
            _ => ModelProviderType.AzureOpenAI
        };

    private static bool TryParseConditionType(string? value, out RoutingRuleConditionType conditionType) =>
        Enum.TryParse(value, ignoreCase: true, out conditionType);

    private static JsonObject BuildEvaluationRequest(string? prompt, string? systemMessage, bool stream, string? requestedModel)
    {
        var jsonRequest = new JsonObject
        {
            ["stream"] = stream
        };

        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            jsonRequest["model"] = requestedModel.Trim();
        }

        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemMessage.Trim()
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = prompt ?? string.Empty
        });
        jsonRequest["messages"] = messages;

        return jsonRequest;
    }

    private static string? ExtractMatchedRuleName(string reason)
    {
        const string marker = "Matched rule '";
        var start = reason.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = reason.IndexOf('\'', start);
        return end < 0 ? null : reason[start..end];
    }

    private static string BuildExplanation(
        RoutingExecutionTrace trace,
        string? matchedRule,
        string classifierSource,
        string? processorModel)
    {
        var model = trace.Decision.ProfileName;
        var classification = trace.Classification;

        var semanticReason = GetContextValue(trace, "semantic.reason");
        if (!string.IsNullOrWhiteSpace(matchedRule) && !string.IsNullOrWhiteSpace(semanticReason))
        {
            return $"The local processor model read the user request and selected rule '{matchedRule}' → routed to '{model}'. Reason: {semanticReason}";
        }

        var classifierPhrase = classifierSource switch
        {
            "processor-model" when !string.IsNullOrWhiteSpace(processorModel) =>
                $"processor '{processorModel}' classified intent={classification.Intent} ({classification.Confidence:0.00})",
            "processor-model" =>
                $"the processor model classified intent={classification.Intent} ({classification.Confidence:0.00})",
            _ when !string.IsNullOrWhiteSpace(classification.Intent) =>
                $"the deterministic classifier detected intent={classification.Intent} ({classification.Confidence:0.00})",
            _ => string.Empty
        };

        var prefix = string.IsNullOrWhiteSpace(classifierPhrase) ? string.Empty : $"{classifierPhrase} → ";

        if (!string.IsNullOrWhiteSpace(matchedRule))
        {
            return $"{prefix}rule '{matchedRule}' matched → routed to '{model}'.";
        }

        var reason = trace.Decision.Reason;
        if (reason.Contains("Explicit", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix}client explicitly requested → routed to '{model}'.";
        }

        if (reason.Contains("Default", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix}no rule matched → routed to default model '{model}'.";
        }

        if (reason.Contains("Fallback", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix}no rule or default matched → fell back to '{model}'.";
        }

        return $"{prefix}routed to '{model}'. {reason}".Trim();
    }

    private static async Task<ModelConnectionTestResult> ProbeModelAsync(
        IChatCompletionsProviderFactory providerFactory,
        ModelProfileOptions profile,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = profile.Deployment,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "ping" }
            }
        };

        // Newer Azure OpenAI models (gpt-5 family) reject the legacy `max_tokens`
        // parameter and require `max_completion_tokens`; Ollama uses `max_tokens`.
        if (profile.Type == ModelProviderType.AzureOpenAI)
        {
            payload["max_completion_tokens"] = 16;
        }
        else
        {
            payload["max_tokens"] = 1;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var response = await providerFactory
                .GetProvider(profile)
                .SendChatCompletionsAsync(payload, profile, stream: false, cancellationToken);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ModelConnectionTestResult(true, $"Connection succeeded ({(int)response.StatusCode}).", stopwatch.Elapsed.TotalMilliseconds);
            }

            var detail = await ReadUpstreamErrorAsync(response, cancellationToken);
            var message = string.IsNullOrWhiteSpace(detail)
                ? $"Upstream returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : $"Upstream returned {(int)response.StatusCode} {response.ReasonPhrase}: {detail}";
            return new ModelConnectionTestResult(false, message, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ModelConnectionTestResult(false, $"Connection failed: {ex.Message}", stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task<string?> ReadUpstreamErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // OpenAI/Azure error envelopes wrap the human-readable text in error.message.
            try
            {
                if (JsonNode.Parse(raw) is JsonObject obj
                    && obj["error"] is JsonObject error
                    && error["message"] is JsonValue messageValue)
                {
                    return messageValue.ToString();
                }
            }
            catch (JsonException)
            {
                // Fall through to the trimmed raw body.
            }

            return raw.Length > 400 ? raw[..400] : raw;
        }
        catch
        {
            return null;
        }
    }

    private static BasicRulesDto ToBasicRulesDto(BasicRulesConfiguration rules) =>
        new(
            rules.DefaultProfile,
            rules.BigPromptCharacterThreshold,
            rules.BigProfile,
            rules.StreamingProfile,
            rules.PreferBigWhenSystemMessageExists,
            rules.PreferStreamingProfileWhenStreaming,
            rules.UpdatedAtUtc);

    private static ConnectedClientDto ToConnectedClientDto(ConnectedClientSnapshot snapshot) =>
        new(
            snapshot.Client,
            snapshot.IsConnected,
            snapshot.ActiveRequests,
            snapshot.RequestsLastFiveMinutes,
            snapshot.LastSeenAtUtc);

    private static LiveRequestDto ToLiveRequestDto(LiveRequestSnapshot snapshot) =>
        new(
            snapshot.RequestId,
            snapshot.Endpoint,
            snapshot.Client,
            snapshot.Stream,
            snapshot.RequestedModel,
            snapshot.SelectedProfile,
            snapshot.SelectedDeployment,
            snapshot.TraceId,
            snapshot.StartedAtUtc,
            snapshot.ElapsedMs);

    private static string? GetContextValue(RoutingExecutionTrace trace, string key) =>
        trace.Context.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string GetClientDisplayName(string clientId) => GetClientDisplayName(clientId, null);

    private static string GetClientDisplayName(string clientId, string? userAgent)
    {
        var byId = clientId.ToLowerInvariant() switch
        {
            "vscode" => "VS Code Copilot",
            "vscode-copilot" => "VS Code Copilot",
            "copilot-cli" => "Copilot CLI",
            "copilot-app" => "Copilot App",
            _ => null
        };

        if (byId is not null)
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            var ua = userAgent.ToLowerInvariant();
            if (ua.Contains("vscode") || ua.Contains("visual studio code") || ua.Contains("copilot-chat"))
            {
                return "VS Code Copilot";
            }

            if (ua.Contains("copilot"))
            {
                return "GitHub Copilot";
            }
        }

        return string.IsNullOrWhiteSpace(clientId) || clientId == "unknown" ? "Unknown client" : clientId;
    }

    // ── Phase 8 mapping helpers ───────────────────────────────────────────────

    private static RuleRecommendationDto ApprovalToRecommendationDto(ApprovalRequestSummary a)
    {
        string ruleKey = a.ChangeType, currentValue = "", recommendedValue = "", rationale = a.Description;
        double confidence = 0.75;

        try
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(a.PayloadJson);
            ruleKey = TryGetString(payload, "ruleKey") ?? a.ChangeType;
            currentValue = TryGetString(payload, "currentValue") ?? "";
            recommendedValue = TryGetString(payload, "recommendedValue") ?? "";
            rationale = TryGetString(payload, "rationale") ?? a.Description;
            if (payload.TryGetProperty("confidence", out var conf)) confidence = conf.GetDouble();
        }
        catch { /* fall through to defaults */ }

        return new RuleRecommendationDto(a.ApprovalId, ruleKey, currentValue, recommendedValue, rationale, confidence, a.Status, a.CreatedAtUtc);
    }

    private static string? TryGetString(System.Text.Json.JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String
            ? prop.GetString()
            : null;

    private static AdminTeamProfileDto ToAdminTeamDto(TeamProfileSummary t)
    {
        IReadOnlyList<string> preferredModels;
        try
        {
            preferredModels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.RulesJson) ?? [t.DefaultProfile];
        }
        catch
        {
            preferredModels = [t.DefaultProfile];
        }

        return new AdminTeamProfileDto(t.TeamId, t.DisplayName, preferredModels, false);
    }

    private static AdminProjectProfileDto ToAdminProjectDto(ProjectProfileSummary p)
    {
        IReadOnlyList<string> tags;
        try
        {
            tags = System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.RulesJson) ?? [];
        }
        catch
        {
            tags = [];
        }

        return new AdminProjectProfileDto(p.ProjectId, p.TeamId, tags, p.DefaultProfile);
    }
}
