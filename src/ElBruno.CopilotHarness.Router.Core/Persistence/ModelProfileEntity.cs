namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class ModelProfileEntity
{
    public string ProfileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-10-21";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
