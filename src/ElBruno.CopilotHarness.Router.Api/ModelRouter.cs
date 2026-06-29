using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

public interface IModelRouter
{
    RoutingDecision SelectModel(JsonObject requestBody);
}

public sealed class BasicModelRouter(IOptions<RoutingOptions> options) : IModelRouter
{
    private readonly RoutingOptions _options = options.Value;

    public RoutingDecision SelectModel(JsonObject requestBody) => SelectModel(requestBody, _options);

    public static RoutingDecision SelectModel(JsonObject requestBody, RoutingOptions options) =>
        SelectModel(requestBody, options, intent: null);

    public static RoutingDecision SelectModel(JsonObject requestBody, RoutingOptions options, string? intent)
    {
        var requestedModel = GetStringValue(requestBody["model"]);
        if (!string.IsNullOrWhiteSpace(requestedModel) &&
            TryGetEnabledProfile(options, requestedModel, out var explicitProfileName, out var explicitProfile))
        {
            return new RoutingDecision(explicitProfileName, explicitProfile, "Explicit model profile requested by client.");
        }

        // Condition-based rules take precedence when configured.
        if (options.RuleSet.Count > 0)
        {
            foreach (var rule in options.RuleSet.Where(rule => rule.Enabled).OrderBy(rule => rule.Priority))
            {
                if (Matches(rule, requestBody, intent) &&
                    TryGetEnabledProfile(options, rule.TargetModel, out var ruleProfileName, out var ruleProfile))
                {
                    return new RoutingDecision(ruleProfileName, ruleProfile, $"Matched rule '{rule.Name}'.");
                }
            }
        }
        else
        {
            // Legacy fixed-rule fallback for back-compat when no condition rules exist.
            if (options.Rules.PreferBigWhenSystemMessageExists &&
                ContainsSystemMessage(requestBody) &&
                TryGetEnabledProfile(options, options.Rules.BigProfile, out var systemProfileName, out var systemProfile))
            {
                return new RoutingDecision(systemProfileName, systemProfile, "System message detected by basic rule.");
            }

            if (GetPromptCharacterCount(requestBody) >= options.Rules.BigPromptCharacterThreshold &&
                TryGetEnabledProfile(options, options.Rules.BigProfile, out var bigProfileName, out var bigProfile))
            {
                return new RoutingDecision(bigProfileName, bigProfile, "Prompt size exceeded threshold.");
            }

            var legacyStream = requestBody["stream"]?.GetValue<bool>() ?? false;
            if (legacyStream &&
                options.Rules.PreferStreamingProfileWhenStreaming &&
                TryGetEnabledProfile(options, options.Rules.StreamingProfile, out var streamingProfileName, out var streamingProfile))
            {
                return new RoutingDecision(streamingProfileName, streamingProfile, "Streaming request matched basic streaming rule.");
            }
        }

        if (TryGetEnabledProfile(options, options.DefaultProfile, out var defaultProfileName, out var defaultProfile))
        {
            return new RoutingDecision(defaultProfileName, defaultProfile, "Default model profile.");
        }

        var fallback = options.Profiles.FirstOrDefault(profile => profile.Value.Enabled);
        if (fallback.Equals(default(KeyValuePair<string, ModelProfileOptions>)))
        {
            throw new InvalidOperationException("No enabled models are configured.");
        }

        return new RoutingDecision(fallback.Key, fallback.Value, "Fallback to first enabled model.");
    }

    private static bool Matches(RoutingRuleDefinition rule, JsonObject requestBody, string? intent) =>
        rule.ConditionType switch
        {
            RoutingRuleConditionType.Always => true,
            RoutingRuleConditionType.IsStreaming => requestBody["stream"]?.GetValue<bool>() ?? false,
            RoutingRuleConditionType.HasSystemMessage => ContainsSystemMessage(requestBody),
            RoutingRuleConditionType.PromptSizeAtLeast =>
                int.TryParse(rule.ConditionValue, out var threshold) && GetPromptCharacterCount(requestBody) >= threshold,
            RoutingRuleConditionType.RequestedModelEquals =>
                string.Equals(GetStringValue(requestBody["model"]), rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            RoutingRuleConditionType.PromptContainsKeyword =>
                !string.IsNullOrWhiteSpace(rule.ConditionValue) &&
                GetPromptText(requestBody).Contains(rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            RoutingRuleConditionType.PromptMatchesRegex => MatchesRegex(rule.ConditionValue, GetPromptText(requestBody)),
            RoutingRuleConditionType.IntentEquals =>
                !string.IsNullOrWhiteSpace(intent) &&
                string.Equals(intent, rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool MatchesRegex(string pattern, string input)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool TryGetEnabledProfile(
        RoutingOptions options,
        string profileName,
        out string selectedProfileName,
        out ModelProfileOptions profile)
    {
        selectedProfileName = profileName;
        profile = null!;

        if (!options.TryGetProfile(profileName, out var configuredProfile) || !configuredProfile.Enabled)
        {
            return false;
        }

        selectedProfileName = options.Profiles.Keys.First(key => string.Equals(key, profileName, StringComparison.OrdinalIgnoreCase));
        profile = configuredProfile;
        return true;
    }

    private static bool ContainsSystemMessage(JsonObject requestBody)
    {
        if (requestBody["messages"] is not JsonArray messages)
        {
            return false;
        }

        return messages
            .OfType<JsonObject>()
            .Any(message => string.Equals(GetStringValue(message["role"]), "system", StringComparison.OrdinalIgnoreCase));
    }

    internal static int GetPromptCharacterCount(JsonObject requestBody)
    {
        if (requestBody["messages"] is not JsonArray messages)
        {
            return 0;
        }

        var count = 0;
        foreach (var message in messages.OfType<JsonObject>())
        {
            if (message["content"] is JsonValue singleContent && singleContent.TryGetValue<string>(out var contentText))
            {
                count += contentText.Length;
                continue;
            }

            if (message["content"] is not JsonArray multiPartContent)
            {
                continue;
            }

            foreach (var part in multiPartContent.OfType<JsonObject>())
            {
                if (part["text"] is JsonValue textPart && textPart.TryGetValue<string>(out var text))
                {
                    count += text.Length;
                }
            }
        }

        return count;
    }

    internal static string GetPromptText(JsonObject requestBody)
    {
        if (requestBody["messages"] is not JsonArray messages)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var message in messages.OfType<JsonObject>())
        {
            if (message["content"] is JsonValue singleContent && singleContent.TryGetValue<string>(out var contentText))
            {
                builder.Append(contentText).Append('\n');
                continue;
            }

            if (message["content"] is not JsonArray multiPartContent)
            {
                continue;
            }

            foreach (var part in multiPartContent.OfType<JsonObject>())
            {
                if (part["text"] is JsonValue textPart && textPart.TryGetValue<string>(out var text))
                {
                    builder.Append(text).Append('\n');
                }
            }
        }

        return builder.ToString();
    }

    private static string? GetStringValue(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return null;
    }
}

public sealed record RoutingDecision(string ProfileName, ModelProfileOptions Profile, string Reason);
