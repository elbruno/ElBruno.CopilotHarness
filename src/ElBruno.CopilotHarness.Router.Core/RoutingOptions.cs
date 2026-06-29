using System.ComponentModel.DataAnnotations;

namespace ElBruno.CopilotHarness.Router.Core;

public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    [Required]
    public string DefaultProfile { get; init; } = "small";

    [Required]
    public Dictionary<string, ModelProfileOptions> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public BasicRulesOptions Rules { get; init; } = new();

    /// <summary>
    /// Ordered, condition-based routing rules. When present, these take precedence over the
    /// legacy <see cref="BasicRulesOptions"/> mapping. Each rule targets a model by name.
    /// </summary>
    public IReadOnlyList<RoutingRuleDefinition> RuleSet { get; init; } = [];

    public bool TryGetProfile(string profileName, out ModelProfileOptions profile) =>
        Profiles.TryGetValue(profileName, out profile!);
}

public sealed class ModelProfileOptions
{
    /// <summary>Provider connection type. Defaults to Azure OpenAI / Foundry for backward compatibility.</summary>
    public ModelProviderType Type { get; init; } = ModelProviderType.AzureOpenAI;

    /// <summary>
    /// Provider endpoint. When empty for an Azure connection, the shared Foundry endpoint is used.
    /// For Ollama this is the base URL of the Ollama server (e.g. http://localhost:11434).
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Azure deployment name or Ollama model name used as the upstream model identifier.
    /// </summary>
    [Required]
    public string Deployment { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = "2024-10-21";

    /// <summary>Decrypted API key for this connection. Empty for Ollama or when the shared Foundry key is used.</summary>
    public string ApiKey { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
}

public sealed class BasicRulesOptions
{
    public int BigPromptCharacterThreshold { get; init; } = 2500;

    [Required]
    public string BigProfile { get; init; } = "big";

    [Required]
    public string StreamingProfile { get; init; } = "small";

    public bool PreferBigWhenSystemMessageExists { get; init; } = true;

    public bool PreferStreamingProfileWhenStreaming { get; init; } = true;
}

/// <summary>
/// Condition that a routing rule evaluates against an incoming chat completions request.
/// </summary>
public enum RoutingRuleConditionType
{
    /// <summary>Always matches. Useful as a catch-all default rule.</summary>
    Always = 0,

    /// <summary>Matches when the prompt character count is greater than or equal to the numeric value.</summary>
    PromptSizeAtLeast = 1,

    /// <summary>Matches when the request is a streaming request.</summary>
    IsStreaming = 2,

    /// <summary>Matches when the request contains a system message.</summary>
    HasSystemMessage = 3,

    /// <summary>Matches when the client-requested model equals the value (case-insensitive).</summary>
    RequestedModelEquals = 4,

    /// <summary>Matches when any prompt text contains the keyword (case-insensitive).</summary>
    PromptContainsKeyword = 5,

    /// <summary>Matches when any prompt text matches the regular expression value.</summary>
    PromptMatchesRegex = 6
}

/// <summary>
/// Runtime representation of a condition-based routing rule that targets a model by name.
/// </summary>
public sealed record RoutingRuleDefinition(
    int Id,
    string Name,
    string Description,
    RoutingRuleConditionType ConditionType,
    string ConditionValue,
    string TargetModel,
    int Priority,
    bool Enabled);
