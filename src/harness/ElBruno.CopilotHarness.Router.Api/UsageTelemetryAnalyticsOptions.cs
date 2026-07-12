namespace ElBruno.CopilotHarness.Router.Api;

public sealed class UsageTelemetryAnalyticsOptions
{
    public const string SectionName = "Telemetry:UsageAnalytics";
    public UsageTelemetryRetentionOptions Retention { get; set; } = new();
}

public sealed class UsageTelemetryRetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int MaxRowsPerRun { get; set; } = 5000;

    public ElBruno.CopilotHarness.Router.Core.Persistence.UsageTelemetryRetentionPolicy ToPolicy() =>
        new(Enabled, RetentionDays, MaxRowsPerRun);
}
