namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class UsagePricingCardEntity
{
    public long Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Operation { get; set; } = "chat";
    public double InputUsdPer1MToken { get; set; }
    public double OutputUsdPer1MToken { get; set; }
    public DateTimeOffset EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string? SourceMetadataJson { get; set; }
    public bool IsOverride { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
