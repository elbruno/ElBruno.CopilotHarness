namespace ElBruno.CopilotHarness.Router.Api.Admin;

public sealed record UsageTelemetryIngestRequest(
    string IdempotencyKey,
    DateTimeOffset OccurredAtUtc,
    string Proxy,
    string Provider,
    string RequestModel,
    string? ResponseModel,
    string? TraceId,
    string? SpanId,
    string Operation,
    int? StatusCode,
    bool Succeeded,
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens);

public sealed record UsageTelemetryIngestResponse(
    bool Accepted,
    bool Duplicate,
    string IdempotencyKey,
    int RetentionDeletedRows);

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

public sealed record UsagePricingOverrideRequest(
    string Provider,
    string Model,
    string Operation,
    double InputUsdPer1MToken,
    double OutputUsdPer1MToken,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string UpdatedBy,
    string Reason,
    string? SourceReference);

public sealed record UsagePricingRefreshAzureRetailRequest(
    string UpdatedBy,
    string? SourceReference);

public sealed record UsagePricingUpsertResponse(
    int ChangedRows);

public sealed record UsagePricingRefreshResponse(
    bool Applied,
    int ChangedRows,
    int ParsedRows,
    int SkippedRows,
    string Source);

public sealed record UsageTelemetryRetentionResponse(
    bool Applied,
    int DeletedRows);
