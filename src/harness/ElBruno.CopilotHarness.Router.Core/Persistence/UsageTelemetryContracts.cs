namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed record UsageTelemetryEventRecord(
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

public sealed record UsageTelemetryIngestResult(
    bool Accepted,
    bool Duplicate,
    string IdempotencyKey);

public sealed record UsageTelemetrySummaryWindow(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long EventCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    IReadOnlyList<UsageTelemetrySummaryRow> Rows);

public sealed record UsageTelemetrySummaryRow(
    string Proxy,
    string Provider,
    string Model,
    long EventCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens);

public sealed record UsageTelemetryEventWindowItem(
    DateTimeOffset OccurredAtUtc,
    string Proxy,
    string Provider,
    string Model,
    long InputTokens,
    long OutputTokens,
    long TotalTokens);

public sealed record UsageTelemetryRetentionPolicy(
    bool Enabled,
    int RetentionDays,
    int MaxRowsPerRun)
{
    public DateTimeOffset CutoffUtc(DateTimeOffset nowUtc) => nowUtc.AddDays(-RetentionDays);
}

public static class UsageTelemetryValidator
{
    public static UsageTelemetryEventRecord Validate(UsageTelemetryEventRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.Proxy))
        {
            throw new ArgumentException("Proxy is required.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.Provider))
        {
            throw new ArgumentException("Provider is required.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.RequestModel))
        {
            throw new ArgumentException("RequestModel is required.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.Operation))
        {
            throw new ArgumentException("Operation is required.", nameof(record));
        }

        if (record.InputTokens is < 0 || record.OutputTokens is < 0 || record.TotalTokens is < 0)
        {
            throw new ArgumentException("Token values cannot be negative.", nameof(record));
        }

        var computedTotal = record.TotalTokens ?? ((record.InputTokens ?? 0L) + (record.OutputTokens ?? 0L));
        return record with
        {
            IdempotencyKey = record.IdempotencyKey.Trim(),
            Proxy = record.Proxy.Trim(),
            Provider = record.Provider.Trim(),
            RequestModel = record.RequestModel.Trim(),
            ResponseModel = string.IsNullOrWhiteSpace(record.ResponseModel) ? null : record.ResponseModel.Trim(),
            TraceId = string.IsNullOrWhiteSpace(record.TraceId) ? null : record.TraceId.Trim(),
            SpanId = string.IsNullOrWhiteSpace(record.SpanId) ? null : record.SpanId.Trim(),
            Operation = record.Operation.Trim(),
            TotalTokens = computedTotal
        };
    }
}

public interface IUsageTelemetryStore
{
    Task<UsageTelemetryIngestResult> IngestAsync(UsageTelemetryEventRecord record, CancellationToken cancellationToken);
    Task<UsageTelemetrySummaryWindow> GetSummaryAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? proxy,
        string? provider,
        string? model,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<UsageTelemetryEventWindowItem>> GetEventsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? proxy,
        string? provider,
        string? model,
        CancellationToken cancellationToken);
    Task<int> ApplyRetentionPolicyAsync(UsageTelemetryRetentionPolicy policy, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
