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

            if (GetUserPromptCharacterCount(requestBody) >= options.Rules.BigPromptCharacterThreshold &&
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
                int.TryParse(rule.ConditionValue, out var threshold) && GetUserPromptCharacterCount(requestBody) >= threshold,
            RoutingRuleConditionType.RequestedModelEquals =>
                string.Equals(GetStringValue(requestBody["model"]), rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            RoutingRuleConditionType.PromptContainsKeyword =>
                !string.IsNullOrWhiteSpace(rule.ConditionValue) &&
                GetUserPromptText(requestBody).Contains(rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            RoutingRuleConditionType.PromptMatchesRegex => MatchesRegex(rule.ConditionValue, GetUserPromptText(requestBody)),
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

    /// <summary>Public wrapper over the enabled-profile lookup, for the semantic analyzer.</summary>
    public static bool TryResolveProfile(
        RoutingOptions options,
        string profileName,
        out string selectedProfileName,
        out ModelProfileOptions profile) =>
        TryGetEnabledProfile(options, profileName, out selectedProfileName, out profile);

    /// <summary>
    /// Returns the enabled <see cref="RoutingRuleConditionType.SemanticMatch"/> rules in evaluation
    /// order (ascending priority). The last/highest-priority-number rule acts as the catch-all.
    /// </summary>
    public static IReadOnlyList<RoutingRuleDefinition> GetSemanticRules(RoutingOptions options) =>
        options.RuleSet
            .Where(rule => rule is { Enabled: true, ConditionType: RoutingRuleConditionType.SemanticMatch })
            .OrderBy(rule => rule.Priority)
            .ToList();

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

    public static int GetPromptCharacterCount(JsonObject requestBody)
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

    /// <summary>
    /// Extracts the plain text of a single chat message, handling both the simple
    /// string form and the multi-part content array form.
    /// </summary>
    internal static string GetMessageText(JsonObject message)
    {
        if (message["content"] is JsonValue singleContent && singleContent.TryGetValue<string>(out var contentText))
        {
            return contentText;
        }

        if (message["content"] is not JsonArray multiPartContent)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var part in multiPartContent.OfType<JsonObject>())
        {
            if (part["text"] is JsonValue textPart && textPart.TryGetValue<string>(out var text))
            {
                builder.Append(text).Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the text of the <b>last user message</b> — the actual turn the caller typed.
    /// GitHub Copilot prepends a large boilerplate system preamble to every request, so
    /// routing and classification must look at the user's message, not the whole payload.
    /// Copilot Chat additionally wraps the typed text in a <c>&lt;userRequest&gt;</c> tag and
    /// surrounds it with <c>&lt;attachments&gt;</c>, <c>&lt;context&gt;</c>, and
    /// <c>&lt;reminderInstructions&gt;</c> blocks; this method extracts the typed text so a
    /// one-word ask like "hi" is not inflated to ~1000 characters of tool/repo context.
    /// Falls back to the full prompt text when no user message is present.
    /// </summary>
    public static string GetUserPromptText(JsonObject requestBody) =>
        ExtractTypedUserMessage(GetRawUserMessageText(requestBody));

    /// <summary>
    /// Returns the <b>raw</b> last user message, including any Copilot Chat wrapper blocks
    /// (<c>&lt;attachments&gt;</c>, <c>&lt;context&gt;</c>, <c>&lt;reminderInstructions&gt;</c>,
    /// <c>&lt;userRequest&gt;</c>). Used by the Live view to show the complete payload Copilot
    /// sent, alongside the cleaned typed message.
    /// </summary>
    public static string GetRawUserMessageText(JsonObject requestBody)
    {
        if (requestBody["messages"] is JsonArray messages)
        {
            var lastUser = messages
                .OfType<JsonObject>()
                .LastOrDefault(message => string.Equals(GetStringValue(message["role"]), "user", StringComparison.OrdinalIgnoreCase));

            if (lastUser is not null)
            {
                var text = GetMessageText(lastUser).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        return GetPromptText(requestBody).Trim();
    }

    private static readonly string[] CopilotWrapperTags =
    {
        "attachments", "context", "reminderInstructions", "environment_info",
        "editorContext", "currentEditor", "instructions", "toolResult", "tool-result"
    };

    /// <summary>
    /// Extracts the actual user-typed text from a raw Copilot Chat user message. Prefers the
    /// content of the <c>&lt;userRequest&gt;</c> tag (the VS Code Copilot Chat convention); if
    /// that tag is absent, strips the known wrapper blocks and returns the remaining text;
    /// otherwise returns the trimmed raw message unchanged (non-Copilot clients).
    /// </summary>
    public static string ExtractTypedUserMessage(string rawUserMessage)
    {
        if (string.IsNullOrWhiteSpace(rawUserMessage))
        {
            return rawUserMessage ?? string.Empty;
        }

        var userRequest = ExtractTagContent(rawUserMessage, "userRequest")
            ?? ExtractTagContent(rawUserMessage, "user-request");
        if (!string.IsNullOrWhiteSpace(userRequest))
        {
            return userRequest.Trim();
        }

        // No <userRequest> tag — strip known Copilot wrapper blocks and keep what remains.
        var stripped = rawUserMessage;
        foreach (var tag in CopilotWrapperTags)
        {
            stripped = RemoveTagBlock(stripped, tag);
        }

        stripped = stripped.Trim();
        return string.IsNullOrWhiteSpace(stripped) ? rawUserMessage.Trim() : stripped;
    }

    private static string? ExtractTagContent(string text, string tag)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";
        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var contentStart = start + open.Length;
        var end = text.IndexOf(close, contentStart, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : text[contentStart..end];
    }

    private static string RemoveTagBlock(string text, string tag)
    {
        while (true)
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";
            var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return text;
            }

            var closeIndex = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0)
            {
                return text;
            }

            text = text.Remove(start, closeIndex + close.Length - start);
        }
    }

    /// <summary>
    /// Character count of the typed user message — the size of the actual ask, excluding
    /// the system preamble, prior conversation turns, and Copilot Chat wrapper blocks.
    /// </summary>
    public static int GetUserPromptCharacterCount(JsonObject requestBody) =>
        GetUserPromptText(requestBody).Length;

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
