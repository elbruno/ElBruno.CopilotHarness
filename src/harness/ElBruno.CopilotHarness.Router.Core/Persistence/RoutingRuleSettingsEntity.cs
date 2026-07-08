namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class RoutingRuleSettingsEntity
{
    public int Id { get; set; } = 1;
    public string DefaultProfile { get; set; } = "small";
    public int BigPromptCharacterThreshold { get; set; } = 2500;
    public string BigProfile { get; set; } = "big";
    public string StreamingProfile { get; set; } = "small";
    public bool PreferBigWhenSystemMessageExists { get; set; } = true;
    public bool PreferStreamingProfileWhenStreaming { get; set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
