using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class UsageCostEstimatorUnitTests
{
    private readonly UsageCostEstimator _estimator = new();

    [Fact]
    public void Estimate_SelectsRateByEffectiveDate()
    {
        var cards = new[]
        {
            CreateCard("azure_openai", "gpt-5-mini", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1.0, 2.0),
            CreateCard("azure_openai", "gpt-5-mini", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), 3.0, 4.0)
        };
        var usageEvents = new[]
        {
            CreateEvent("FoundryProxy", "azure_openai", "gpt-5-mini", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), 1_000_000, 1_000_000)
        };

        var result = _estimator.Estimate(usageEvents, cards);
        var row = result.Rows.Single().Value;

        Assert.True(row.EstimateAvailable);
        Assert.NotNull(row.EstimatedUsd);
        Assert.Equal(7.0, row.EstimatedUsd!.Value, precision: 6);
    }

    [Fact]
    public void Estimate_BlocksLocalProviders()
    {
        var usageEvents = new[]
        {
            CreateEvent("OllamaProxy", "ollama", "llama3.1", DateTimeOffset.UtcNow, 1000, 1000)
        };

        var result = _estimator.Estimate(usageEvents, Array.Empty<UsagePricingCard>());
        var row = result.Rows.Single().Value;

        Assert.False(row.EstimateAvailable);
        Assert.Null(row.EstimatedUsd);
        Assert.Equal("provider-not-supported", row.EstimateUnavailableReason);
    }

    [Fact]
    public void Estimate_ReportsUnknownRatesForCloudProvider()
    {
        var usageEvents = new[]
        {
            CreateEvent("FoundryProxy", "azure_openai", "unknown-model", DateTimeOffset.UtcNow, 1000, 1000)
        };

        var result = _estimator.Estimate(usageEvents, Array.Empty<UsagePricingCard>());
        var row = result.Rows.Single().Value;

        Assert.False(row.EstimateAvailable);
        Assert.Null(row.EstimatedUsd);
        Assert.Equal("rate-not-found", row.EstimateUnavailableReason);
    }

    [Fact]
    public void Estimate_PrefersManualOverrideRate()
    {
        var effectiveFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var cards = new[]
        {
            CreateCard("azure_openai", "gpt-5-mini", effectiveFrom, 2.0, 2.0, updatedAt: effectiveFrom, isOverride: false),
            CreateCard("azure_openai", "gpt-5-mini", effectiveFrom, 1.0, 1.0, updatedAt: effectiveFrom.AddMinutes(5), isOverride: true)
        };
        var usageEvents = new[]
        {
            CreateEvent("FoundryProxy", "azure_openai", "gpt-5-mini", effectiveFrom.AddDays(1), 1_000_000, 1_000_000)
        };

        var result = _estimator.Estimate(usageEvents, cards);
        var row = result.Rows.Single().Value;

        Assert.True(row.EstimateAvailable);
        Assert.NotNull(row.EstimatedUsd);
        Assert.Equal(2.0, row.EstimatedUsd!.Value, precision: 6);
    }

    private static UsageTelemetryEventWindowItem CreateEvent(
        string proxy,
        string provider,
        string model,
        DateTimeOffset occurredAtUtc,
        long inputTokens,
        long outputTokens) =>
        new(
            occurredAtUtc,
            proxy,
            provider,
            model,
            inputTokens,
            outputTokens,
            inputTokens + outputTokens);

    private static UsagePricingCard CreateCard(
        string provider,
        string model,
        DateTimeOffset effectiveFromUtc,
        double inputRate,
        double outputRate,
        DateTimeOffset? updatedAt = null,
        bool isOverride = false) =>
        new(
            Id: 1,
            Provider: provider,
            Model: model,
            Operation: "chat",
            InputUsdPer1MToken: inputRate,
            OutputUsdPer1MToken: outputRate,
            EffectiveFromUtc: effectiveFromUtc,
            EffectiveToUtc: null,
            SourceType: isOverride ? "manual-override" : "azure-retail-prices-api",
            SourceReference: "test",
            SourceMetadataJson: null,
            IsOverride: isOverride,
            UpdatedBy: "tests",
            UpdatedAtUtc: updatedAt ?? DateTimeOffset.UtcNow);
}
