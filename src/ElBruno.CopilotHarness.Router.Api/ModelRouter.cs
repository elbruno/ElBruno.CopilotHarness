using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

public interface IModelRouter
{
    RoutingDecision SelectModel(JsonObject requestBody);
}

public sealed class BasicModelRouter(IOptions<RoutingOptions> options) : IModelRouter
{
    private readonly RoutingOptions _options = options.Value;

    public RoutingDecision SelectModel(JsonObject requestBody)
    {
        var requestedModel = GetStringValue(requestBody["model"]);
        if (!string.IsNullOrWhiteSpace(requestedModel) &&
            TryGetEnabledProfile(requestedModel, out var explicitProfileName, out var explicitProfile))
        {
            return new RoutingDecision(explicitProfileName, explicitProfile, "Explicit model profile requested by client.");
        }

        if (_options.Rules.PreferBigWhenSystemMessageExists &&
            ContainsSystemMessage(requestBody) &&
            TryGetEnabledProfile(_options.Rules.BigProfile, out var systemProfileName, out var systemProfile))
        {
            return new RoutingDecision(systemProfileName, systemProfile, "System message detected by basic rule.");
        }

        if (GetPromptCharacterCount(requestBody) >= _options.Rules.BigPromptCharacterThreshold &&
            TryGetEnabledProfile(_options.Rules.BigProfile, out var bigProfileName, out var bigProfile))
        {
            return new RoutingDecision(bigProfileName, bigProfile, "Prompt size exceeded threshold.");
        }

        var stream = requestBody["stream"]?.GetValue<bool>() ?? false;
        if (stream &&
            _options.Rules.PreferStreamingProfileWhenStreaming &&
            TryGetEnabledProfile(_options.Rules.StreamingProfile, out var streamingProfileName, out var streamingProfile))
        {
            return new RoutingDecision(streamingProfileName, streamingProfile, "Streaming request matched basic streaming rule.");
        }

        if (TryGetEnabledProfile(_options.DefaultProfile, out var defaultProfileName, out var defaultProfile))
        {
            return new RoutingDecision(defaultProfileName, defaultProfile, "Default model profile.");
        }

        var fallback = _options.Profiles.FirstOrDefault(profile => profile.Value.Enabled);
        if (fallback.Equals(default(KeyValuePair<string, ModelProfileOptions>)))
        {
            throw new InvalidOperationException("No enabled model profiles are configured.");
        }

        return new RoutingDecision(fallback.Key, fallback.Value, "Fallback to first enabled model profile.");
    }

    private bool TryGetEnabledProfile(string profileName, out string selectedProfileName, out ModelProfileOptions profile)
    {
        selectedProfileName = profileName;
        profile = null!;

        if (!_options.TryGetProfile(profileName, out var configuredProfile) || !configuredProfile.Enabled)
        {
            return false;
        }

        selectedProfileName = _options.Profiles.Keys.First(key => string.Equals(key, profileName, StringComparison.OrdinalIgnoreCase));
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

    private static int GetPromptCharacterCount(JsonObject requestBody)
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
