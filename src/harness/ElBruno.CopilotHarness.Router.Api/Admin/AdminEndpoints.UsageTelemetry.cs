using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapUsageTelemetryEndpoints(RouteGroupBuilder group)
    {
        var usageGroup = group.MapGroup("/telemetry/usage");

        usageGroup.MapPost("/events", async (
            UsageTelemetryIngestRequest request,
            IUsageTelemetryStore store,
            IOptions<UsageTelemetryAnalyticsOptions> options,
            CancellationToken cancellationToken) =>
        {
            UsageTelemetryIngestResult result;
            try
            {
                result = await store.IngestAsync(
                    new UsageTelemetryEventRecord(
                        request.IdempotencyKey,
                        request.OccurredAtUtc,
                        request.Proxy,
                        request.Provider,
                        request.RequestModel,
                        request.ResponseModel,
                        request.TraceId,
                        request.SpanId,
                        request.Operation,
                        request.StatusCode,
                        request.Succeeded,
                        request.InputTokens,
                        request.OutputTokens,
                        request.TotalTokens),
                    cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var deletedRows = await store.ApplyRetentionPolicyAsync(
                options.Value.Retention.ToPolicy(),
                DateTimeOffset.UtcNow,
                cancellationToken);

            return Results.Ok(new UsageTelemetryIngestResponse(
                result.Accepted,
                result.Duplicate,
                result.IdempotencyKey,
                deletedRows));
        });

        usageGroup.MapGet("/summary", async (
            IUsageTelemetryStore store,
            IUsagePricingCatalogStore pricingCatalogStore,
            IUsageCostEstimator estimator,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? proxy,
            string? provider,
            string? model,
            CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var normalizedTo = toUtc ?? now;
            var normalizedFrom = fromUtc ?? normalizedTo.AddDays(-1);

            UsageTelemetrySummaryWindow summary;
            try
            {
                summary = await store.GetSummaryAsync(normalizedFrom, normalizedTo, proxy, provider, model, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            IReadOnlyList<UsageTelemetryEventWindowItem> events;
            try
            {
                events = await store.GetEventsAsync(normalizedFrom, normalizedTo, proxy, provider, model, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var cards = await pricingCatalogStore.GetCardsAsync(provider: null, model: null, cancellationToken);
            var estimateSummary = estimator.Estimate(events, cards);
            var summaryRows = summary.Rows.Select(row =>
            {
                var hasEstimate = estimateSummary.Rows.TryGetValue((row.Proxy, row.Provider, row.Model), out var estimateRow);
                return new UsageTelemetrySummaryRowResponse(
                    row.Proxy,
                    row.Provider,
                    row.Model,
                    row.EventCount,
                    row.InputTokens,
                    row.OutputTokens,
                    row.TotalTokens,
                    hasEstimate && estimateRow!.EstimateAvailable,
                    hasEstimate ? estimateRow!.EstimatedUsd : null,
                    hasEstimate ? estimateRow!.EstimateUnavailableReason : "rate-not-found");
            }).ToList();

            return Results.Ok(new UsageTelemetrySummaryResponse(
                summary.FromUtc,
                summary.ToUtc,
                summary.EventCount,
                summary.InputTokens,
                summary.OutputTokens,
                summary.TotalTokens,
                estimateSummary.EstimatedUsdTotal,
                estimateSummary.HasEstimateGaps,
                "Estimated USD is for planning only and is not billed cost. Unknown rates remain token-only.",
                summaryRows));
        });

        usageGroup.MapPost("/retention/run", async (
            IUsageTelemetryStore store,
            IOptions<UsageTelemetryAnalyticsOptions> options,
            CancellationToken cancellationToken) =>
        {
            var policy = options.Value.Retention.ToPolicy();
            var deletedRows = await store.ApplyRetentionPolicyAsync(policy, DateTimeOffset.UtcNow, cancellationToken);
            return Results.Ok(new UsageTelemetryRetentionResponse(policy.Enabled, deletedRows));
        });

        usageGroup.MapGet("/pricing/cards", async (
            IUsagePricingCatalogStore pricingCatalogStore,
            string? provider,
            string? model,
            CancellationToken cancellationToken) =>
        {
            var cards = await pricingCatalogStore.GetCardsAsync(provider, model, cancellationToken);
            return Results.Ok(cards.Select(card => new UsagePricingCardResponse(
                card.Id,
                card.Provider,
                card.Model,
                card.Operation,
                card.InputUsdPer1MToken,
                card.OutputUsdPer1MToken,
                card.EffectiveFromUtc,
                card.EffectiveToUtc,
                card.SourceType,
                card.SourceReference,
                card.SourceMetadataJson,
                card.IsOverride,
                card.UpdatedBy,
                card.UpdatedAtUtc)).ToList());
        });

        usageGroup.MapPost("/pricing/cards/override", async (
            UsagePricingOverrideRequest request,
            IUsagePricingCatalogStore pricingCatalogStore,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Provider) ||
                string.IsNullOrWhiteSpace(request.Model) ||
                string.IsNullOrWhiteSpace(request.Operation) ||
                string.IsNullOrWhiteSpace(request.UpdatedBy) ||
                string.IsNullOrWhiteSpace(request.Reason))
            {
                return Results.BadRequest(new { error = "provider, model, operation, updatedBy and reason are required." });
            }

            var changedRows = await pricingCatalogStore.UpsertCardsAsync(
                [
                    new UsagePricingCardUpsert(
                        request.Provider.Trim().ToLowerInvariant(),
                        request.Model.Trim().ToLowerInvariant(),
                        request.Operation.Trim().ToLowerInvariant(),
                        request.InputUsdPer1MToken,
                        request.OutputUsdPer1MToken,
                        request.EffectiveFromUtc,
                        request.EffectiveToUtc,
                        "manual-override",
                        request.SourceReference ?? "manual-audit-entry",
                        JsonSerializer.Serialize(new { reason = request.Reason }),
                        true,
                        request.UpdatedBy.Trim(),
                        DateTimeOffset.UtcNow)
                ],
                cancellationToken);

            return Results.Ok(new UsagePricingUpsertResponse(changedRows));
        });

        usageGroup.MapPost("/pricing/refresh/azure-retail", async (
            UsagePricingRefreshAzureRetailRequest request,
            IUsagePricingRefreshService refreshService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.UpdatedBy))
            {
                return Results.BadRequest(new { error = "updatedBy is required." });
            }

            var refresh = await refreshService.RefreshFromAzureRetailAsync(
                request.UpdatedBy.Trim(),
                request.SourceReference ?? "https://prices.azure.com/api/retail/prices",
                cancellationToken);
            return Results.Ok(new UsagePricingRefreshResponse(
                true,
                refresh.ChangedRows,
                refresh.ParsedRows,
                refresh.SkippedRows,
                "azure-retail-prices-api"));
        });
    }
}
