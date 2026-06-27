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

    public bool TryGetProfile(string profileName, out ModelProfileOptions profile) =>
        Profiles.TryGetValue(profileName, out profile!);
}

public sealed class ModelProfileOptions
{
    [Required]
    public string Deployment { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = "2024-10-21";

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
