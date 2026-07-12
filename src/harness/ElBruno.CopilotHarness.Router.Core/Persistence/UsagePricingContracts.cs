namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed record UsagePricingCard(
    long Id,
    string Provider,
    string Model,
    string Operation,
    double InputUsdPer1MToken,
    double OutputUsdPer1MToken,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string SourceType,
    string SourceReference,
    string? SourceMetadataJson,
    bool IsOverride,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc);

public sealed record UsagePricingCardUpsert(
    string Provider,
    string Model,
    string Operation,
    double InputUsdPer1MToken,
    double OutputUsdPer1MToken,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string SourceType,
    string SourceReference,
    string? SourceMetadataJson,
    bool IsOverride,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc);

public sealed record UsageEstimateRow(
    string Proxy,
    string Provider,
    string Model,
    bool EstimateAvailable,
    double? EstimatedUsd,
    string? EstimateUnavailableReason);

public sealed record UsageEstimateSummary(
    double EstimatedUsdTotal,
    bool HasEstimateGaps,
    IReadOnlyDictionary<(string Proxy, string Provider, string Model), UsageEstimateRow> Rows);

public interface IUsagePricingCatalogStore
{
    Task<IReadOnlyList<UsagePricingCard>> GetCardsAsync(string? provider, string? model, CancellationToken cancellationToken);
    Task<int> UpsertCardsAsync(IEnumerable<UsagePricingCardUpsert> cards, CancellationToken cancellationToken);
}

public interface IUsageCostEstimator
{
    UsageEstimateSummary Estimate(
        IReadOnlyList<UsageTelemetryEventWindowItem> events,
        IReadOnlyList<UsagePricingCard> cards);
}
