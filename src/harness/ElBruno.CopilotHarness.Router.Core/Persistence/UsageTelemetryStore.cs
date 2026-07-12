using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class UsageTelemetryStore(HarnessDbContext dbContext) : IUsageTelemetryStore
{
    public async Task<UsageTelemetryIngestResult> IngestAsync(UsageTelemetryEventRecord record, CancellationToken cancellationToken)
    {
        var validated = UsageTelemetryValidator.Validate(record);
        var idempotencyKey = validated.IdempotencyKey;

        var alreadyExists = await dbContext.UsageTelemetryEvents
            .AsNoTracking()
            .AnyAsync(entity => entity.IdempotencyKey == idempotencyKey, cancellationToken);
        if (alreadyExists)
        {
            return new UsageTelemetryIngestResult(true, true, idempotencyKey);
        }

        dbContext.UsageTelemetryEvents.Add(new UsageTelemetryEventEntity
        {
            IdempotencyKey = idempotencyKey,
            OccurredAtUtc = validated.OccurredAtUtc,
            Proxy = validated.Proxy,
            Provider = validated.Provider,
            RequestModel = validated.RequestModel,
            ResponseModel = validated.ResponseModel,
            TraceId = validated.TraceId,
            SpanId = validated.SpanId,
            Operation = validated.Operation,
            StatusCode = validated.StatusCode,
            Succeeded = validated.Succeeded,
            InputTokens = validated.InputTokens ?? 0,
            OutputTokens = validated.OutputTokens ?? 0,
            TotalTokens = validated.TotalTokens ?? 0
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new UsageTelemetryIngestResult(true, false, idempotencyKey);
        }
        catch (DbUpdateException)
        {
            var duplicate = await dbContext.UsageTelemetryEvents
                .AsNoTracking()
                .AnyAsync(entity => entity.IdempotencyKey == idempotencyKey, cancellationToken);
            if (!duplicate)
            {
                throw;
            }

            return new UsageTelemetryIngestResult(true, true, idempotencyKey);
        }
    }

    public async Task<UsageTelemetrySummaryWindow> GetSummaryAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? proxy,
        string? provider,
        string? model,
        CancellationToken cancellationToken)
    {
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException("The toUtc value must be greater than fromUtc.");
        }

        var filteredEvents = await GetFilteredEventsAsync(fromUtc, toUtc, proxy, provider, model, cancellationToken);

        var rows = filteredEvents
            .GroupBy(entity => new
            {
                entity.Proxy,
                entity.Provider,
                Model = entity.ResponseModel ?? entity.RequestModel
            })
            .Select(group => new UsageTelemetrySummaryRow(
                group.Key.Proxy,
                group.Key.Provider,
                group.Key.Model,
                group.LongCount(),
                group.Sum(item => item.InputTokens),
                group.Sum(item => item.OutputTokens),
                group.Sum(item => item.TotalTokens)))
            .OrderByDescending(item => item.TotalTokens)
            .ToList();

        return new UsageTelemetrySummaryWindow(
            fromUtc,
            toUtc,
            rows.Sum(row => row.EventCount),
            rows.Sum(row => row.InputTokens),
            rows.Sum(row => row.OutputTokens),
            rows.Sum(row => row.TotalTokens),
            rows);
    }

    public async Task<IReadOnlyList<UsageTelemetryEventWindowItem>> GetEventsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? proxy,
        string? provider,
        string? model,
        CancellationToken cancellationToken)
    {
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException("The toUtc value must be greater than fromUtc.");
        }

        var events = await GetFilteredEventsAsync(fromUtc, toUtc, proxy, provider, model, cancellationToken);
        return events
            .Select(entity => new UsageTelemetryEventWindowItem(
                entity.OccurredAtUtc,
                entity.Proxy,
                entity.Provider,
                entity.ResponseModel ?? entity.RequestModel,
                entity.InputTokens,
                entity.OutputTokens,
                entity.TotalTokens))
            .ToList();
    }

    public async Task<int> ApplyRetentionPolicyAsync(
        UsageTelemetryRetentionPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!policy.Enabled || policy.RetentionDays <= 0 || policy.MaxRowsPerRun <= 0)
        {
            return 0;
        }

        var cutoffUtc = policy.CutoffUtc(nowUtc);
        var staleRows = await dbContext.UsageTelemetryEvents
            .ToListAsync(cancellationToken);
        staleRows = staleRows
            .Where(entity => entity.OccurredAtUtc < cutoffUtc)
            .OrderBy(entity => entity.OccurredAtUtc)
            .Take(policy.MaxRowsPerRun)
            .ToList();

        if (staleRows.Count == 0)
        {
            return 0;
        }

        dbContext.UsageTelemetryEvents.RemoveRange(staleRows);
        await dbContext.SaveChangesAsync(cancellationToken);
        return staleRows.Count;
    }

    private async Task<List<UsageTelemetryEventEntity>> GetFilteredEventsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? proxy,
        string? provider,
        string? model,
        CancellationToken cancellationToken)
    {
        var events = await dbContext.UsageTelemetryEvents
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var filteredEvents = events
            .Where(entity => entity.OccurredAtUtc >= fromUtc && entity.OccurredAtUtc < toUtc);

        if (!string.IsNullOrWhiteSpace(proxy))
        {
            filteredEvents = filteredEvents.Where(entity => string.Equals(entity.Proxy, proxy, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            filteredEvents = filteredEvents.Where(entity => string.Equals(entity.Provider, provider, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            filteredEvents = filteredEvents.Where(entity =>
                string.Equals(entity.ResponseModel ?? entity.RequestModel, model, StringComparison.Ordinal));
        }

        return filteredEvents.ToList();
    }
}
