namespace ElBruno.CopilotHarness.Analytics.Web.Services;

public sealed record UsageTelemetrySummaryResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long EventCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double EstimatedUsdTotal,
    bool HasEstimateGaps,
    string EstimateDisclaimer,
    IReadOnlyList<UsageTelemetrySummaryRowResponse> Rows);

public sealed record UsageTelemetrySummaryRowResponse(
    string Proxy,
    string Provider,
    string Model,
    long EventCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    bool EstimateAvailable,
    double? EstimatedUsd,
    string? EstimateUnavailableReason);

public sealed record UsagePricingCardResponse(
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

public interface IUsageTelemetryApiClient
{
    Task<UsageTelemetrySummaryResponse> GetSummaryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsagePricingCardResponse>> GetPricingCardsAsync(CancellationToken cancellationToken = default);
}
